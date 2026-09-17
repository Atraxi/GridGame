<#
.SYNOPSIS
    One-time bootstrap for deploying GridGame to Azure on free-tier resources.

.DESCRIPTION
    Everything this script creates is either free or a credential that lives outside the repository:

      * a resource group to hold the deployment
      * an Entra app registration whose only way in is GitHub OIDC - no client secret exists
      * two federated credentials, one per job in .github/workflows/deploy.yml
      * a Contributor role assignment scoped to that single resource group
      * randomly generated SQL and JWT secrets, stored as GitHub Actions secrets

    After this runs, every subsequent deployment is a push to master. The infrastructure itself is
    described by infra/main.bicep and applied by the workflow, so this script never needs to run
    again unless you are recreating the environment from scratch.

    Re-running it is safe: existing objects are reused rather than duplicated. Secrets are only
    regenerated if you pass -RotateSecrets, because rotating the JWT key signs everyone out.

.PARAMETER SubscriptionId
    Azure subscription to deploy into. Defaults to the current `az account show` subscription.

.PARAMETER ResourceGroup
    Resource group to create and deploy into.

.PARAMETER Location
    Azure region. Must offer both Linux F1 App Service and the Azure SQL free offer.

.PARAMETER Repository
    GitHub repository in owner/name form. Defaults to whatever `gh repo view` reports for the
    current directory.

.PARAMETER Branch
    Branch the deploy workflow runs from.

.PARAMETER RotateSecrets
    Generate and store new SQL and JWT secrets even if the GitHub secrets already exist.

.PARAMETER DeployNow
    Run the Bicep deployment immediately instead of waiting for the next push.

.EXAMPLE
    ./scripts/setup-azure.ps1 -DeployNow
#>
[CmdletBinding()]
param(
    [string] $SubscriptionId,
    [string] $ResourceGroup = 'gridgame-rg',
    [string] $Location = 'eastus2',
    [string] $Repository,
    [string] $Branch = 'master',
    [switch] $RotateSecrets,
    [switch] $DeployNow
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Tool([string] $Name, [string] $InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not on PATH. $InstallHint"
    }
}

# $ErrorActionPreference has no effect on native executables, so failures have to be checked explicitly
# or the script cheerfully carries on with half a configuration.
function Assert-LastExitCode([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE." }
}

# Get-Random is seeded from the clock and is not suitable for a credential, so every random choice
# below goes through the cryptographic RNG. Create()/GetBytes rather than the static Fill(), which
# Windows PowerShell 5.1 does not have.
function Get-RandomBytes([int] $Count) {
    $bytes = New-Object byte[] $Count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    , $bytes
}

# A password that satisfies Azure SQL's complexity rule (three of upper/lower/digit/symbol) while
# avoiding characters that would need escaping inside an ADO.NET connection string.
function New-SqlPassword {
    $upper = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZ'
    $lower = [char[]]'abcdefghijkmnopqrstuvwxyz'
    $digit = [char[]]'23456789'
    $symbol = [char[]]'!#$%&*+-_?'
    $all = $upper + $lower + $digit + $symbol

    # One character from each class up front guarantees complexity; the shuffle afterwards stops that
    # from being a predictable prefix. Alphabet sizes are all well under 256, so the modulo bias over a
    # single byte is negligible at this length.
    $alphabets = @($upper, $lower, $digit, $symbol)
    1..24 | ForEach-Object { $alphabets += , $all }

    $bytes = Get-RandomBytes ($alphabets.Count * 2)
    $chars = New-Object char[] $alphabets.Count
    for ($i = 0; $i -lt $alphabets.Count; $i++) {
        $chars[$i] = $alphabets[$i][$bytes[$i] % $alphabets[$i].Length]
    }

    # Fisher-Yates, drawing its swap indices from the second half of the same random block.
    for ($i = $chars.Count - 1; $i -gt 0; $i--) {
        $j = $bytes[$alphabets.Count + $i] % ($i + 1)
        $swap = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $swap
    }

    -join $chars
}

function New-SigningKey {
    -join ((Get-RandomBytes 32) | ForEach-Object { $_.ToString('x2') })
}

Write-Step 'Checking prerequisites'
Assert-Tool 'az' 'Install it from https://aka.ms/installazurecli, then run `az login`.'
Assert-Tool 'gh' 'Install it from https://cli.github.com, then run `gh auth login`.'

$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) { throw 'Not signed in to Azure. Run `az login` first.' }

if ($SubscriptionId) {
    az account set --subscription $SubscriptionId | Out-Null
    $account = az account show --output json | ConvertFrom-Json
}
$SubscriptionId = $account.id
$tenantId = $account.tenantId
Write-Host "Subscription: $($account.name) ($SubscriptionId)"

if (-not $Repository) {
    $Repository = gh repo view --json nameWithOwner --jq .nameWithOwner
    if ($LASTEXITCODE -ne 0 -or -not $Repository) {
        throw 'Could not determine the GitHub repository. Pass -Repository owner/name.'
    }
}
Write-Host "Repository:   $Repository"

Write-Step "Creating resource group '$ResourceGroup' in $Location"
az group create --name $ResourceGroup --location $Location --output none
$resourceGroupId = az group show --name $ResourceGroup --query id --output tsv

# --- Entra app registration used by GitHub Actions -----------------------------------------------
# Credential-free by construction: the app has no client secret and no certificate, only federated
# credentials that trust tokens GitHub issues for this specific repository and branch/environment.

$appDisplayName = "github-$($Repository.Replace('/', '-'))"

Write-Step "Configuring Entra app registration '$appDisplayName'"
$appId = az ad app list --display-name $appDisplayName --query "[0].appId" --output tsv
if (-not $appId) {
    $appId = az ad app create --display-name $appDisplayName --query appId --output tsv
    Write-Host "Created app registration $appId"
} else {
    Write-Host "Reusing existing app registration $appId"
}

$servicePrincipalId = az ad sp list --filter "appId eq '$appId'" --query "[0].id" --output tsv
if (-not $servicePrincipalId) {
    $servicePrincipalId = az ad sp create --id $appId --query id --output tsv
    Write-Host 'Created service principal'
}

# The two jobs in deploy.yml present different OIDC subjects: the infrastructure job is identified by
# its branch, while the deploy job runs under a GitHub environment and is identified by that instead.
# Both need a credential or the workflow fails halfway through.
$federatedCredentials = @(
    @{ name = 'github-branch'; subject = "repo:$($Repository):ref:refs/heads/$Branch" }
    @{ name = 'github-environment-production'; subject = "repo:$($Repository):environment:production" }
)

$existingCredentials = az ad app federated-credential list --id $appId --output json | ConvertFrom-Json
foreach ($credential in $federatedCredentials) {
    if ($existingCredentials | Where-Object { $_.subject -eq $credential.subject }) {
        Write-Host "Federated credential already present for $($credential.subject)"
        continue
    }

    $body = @{
        name      = $credential.name
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $credential.subject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json -Compress

    #WriteAllText rather than Set-Content, whose utf8 encoding emits a BOM on PowerShell 5.1
    $bodyFile = New-TemporaryFile
    [System.IO.File]::WriteAllText($bodyFile.FullName, $body)
    az ad app federated-credential create --id $appId --parameters "@$($bodyFile.FullName)" --output none
    Remove-Item $bodyFile
    Write-Host "Added federated credential for $($credential.subject)"
}

Write-Step 'Granting Contributor on the resource group'
$hasRole = az role assignment list --assignee $appId --scope $resourceGroupId --role Contributor --query "[0].id" --output tsv 2>$null
if ($hasRole) {
    Write-Host 'Role assignment already present'
} else {
    # A freshly created service principal takes a few seconds to become visible to the RBAC service.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        az role assignment create --assignee-object-id $servicePrincipalId --assignee-principal-type ServicePrincipal `
            --role Contributor --scope $resourceGroupId --output none 2>$null
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 6) { throw 'Could not assign the Contributor role; check your permissions on the subscription.' }
        Write-Host "Waiting for the service principal to propagate (attempt $attempt)..."
        Start-Sleep -Seconds 10
    }
    Write-Host "Assigned Contributor scoped to $ResourceGroup"
}

# --- GitHub configuration ------------------------------------------------------------------------

Write-Step 'Setting GitHub Actions variables and secrets'
gh variable set AZURE_RESOURCE_GROUP --repo $Repository --body $ResourceGroup
Assert-LastExitCode 'gh variable set AZURE_RESOURCE_GROUP'
gh variable set AZURE_LOCATION --repo $Repository --body $Location
Assert-LastExitCode 'gh variable set AZURE_LOCATION'

gh secret set AZURE_CLIENT_ID --repo $Repository --body $appId
Assert-LastExitCode 'gh secret set AZURE_CLIENT_ID'
gh secret set AZURE_TENANT_ID --repo $Repository --body $tenantId
Assert-LastExitCode 'gh secret set AZURE_TENANT_ID'
gh secret set AZURE_SUBSCRIPTION_ID --repo $Repository --body $SubscriptionId
Assert-LastExitCode 'gh secret set AZURE_SUBSCRIPTION_ID'

# A native command's non-zero exit does not trip $ErrorActionPreference, so this is checked by hand:
# if the listing silently failed, every run would look like a first run and quietly rotate the JWT
# key, signing out every existing session.
$existingSecrets = @(gh secret list --repo $Repository --json name --jq '.[].name')
if ($LASTEXITCODE -ne 0) {
    throw "Could not list the existing GitHub secrets for $Repository. Check 'gh auth status' and that your token has repository admin scope."
}

$sqlPassword = $null
$jwtKey = $null

if ($RotateSecrets -or ($existingSecrets -notcontains 'SQL_ADMIN_PASSWORD')) {
    $sqlPassword = New-SqlPassword
    gh secret set SQL_ADMIN_PASSWORD --repo $Repository --body $sqlPassword
    Assert-LastExitCode 'gh secret set SQL_ADMIN_PASSWORD'
    Write-Host 'Generated a new SQL administrator password'
} else {
    Write-Host 'Keeping the existing SQL_ADMIN_PASSWORD secret'
}

if ($RotateSecrets -or ($existingSecrets -notcontains 'JWT_SIGNING_KEY')) {
    $jwtKey = New-SigningKey
    gh secret set JWT_SIGNING_KEY --repo $Repository --body $jwtKey
    Assert-LastExitCode 'gh secret set JWT_SIGNING_KEY'
    Write-Host 'Generated a new JWT signing key (this signs out every existing session)'
} else {
    Write-Host 'Keeping the existing JWT_SIGNING_KEY secret'
}

# --- Optional first deployment -------------------------------------------------------------------

if ($DeployNow) {
    if (-not $sqlPassword -or -not $jwtKey) {
        throw @'
-DeployNow can only run when this script generated both secrets, because GitHub secrets cannot be
read back. Either add -RotateSecrets, or just push to the branch and let the workflow deploy.
'@
    }

    Write-Step 'Deploying infra/main.bicep'
    az deployment group create `
        --resource-group $ResourceGroup `
        --name 'gridgame-bootstrap' `
        --template-file (Join-Path $repoRoot 'infra/main.bicep') `
        --parameters sqlAdminPassword=$sqlPassword jwtSigningKey=$jwtKey `
        --output none

    $url = az deployment group show --resource-group $ResourceGroup --name 'gridgame-bootstrap' `
        --query properties.outputs.webAppUrl.value --output tsv
    Write-Host "Infrastructure ready at $url (no application deployed yet)" -ForegroundColor Green
}

Write-Step 'Done'
Write-Host @"
Push to '$Branch' (or run the 'Deploy to Azure' workflow manually) to build and deploy.

    gh workflow run deploy.yml --repo $Repository --ref $Branch

Nothing sensitive was written to the working tree. The SQL password and JWT key exist only as
GitHub Actions secrets and as App Service settings; re-run with -RotateSecrets to replace them.
"@ -ForegroundColor Green
