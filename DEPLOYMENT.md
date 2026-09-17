# Deploying GridGame to Azure

Everything runs on free-tier Azure resources, and no secret is ever committed. Infrastructure is
described by [`infra/main.bicep`](infra/main.bicep) and applied by
[`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) on every push to `master`, so the
repository — not the portal — is the source of truth for the running environment.

## Before the first deploy

- **Commit the EF migrations.** `GridGame.Server/GridGameAPI/Database/Migrations` is currently
  untracked. CI builds what is pushed, so if the migrations aren't in the commit the app starts
  against an empty schema and every request fails.
- **Rotate anything that was ever committed.** The JWT signing key that used to live in
  `appsettings.Development.json` is in the public git history; it is dev-only and is no longer used,
  but don't reuse that value anywhere.
- **`Microsoft.OpenApi` 2.0.0 carries a known high-severity advisory** (pulled in transitively by
  Scalar; `NU1903` at build time). Only the Scalar/OpenAPI endpoints are affected and they are
  development-only, but it is worth clearing before putting the app on the public internet.

## One-time setup

Install the [Azure CLI](https://aka.ms/installazurecli) and the [GitHub CLI](https://cli.github.com),
sign in to both (`az login`, `gh auth login`), then from the repository root:

```powershell
./scripts/setup-azure.ps1
```

That creates the resource group, an Entra app registration wired to GitHub via OIDC, a Contributor
role assignment scoped to just that resource group, and the GitHub Actions secrets. Push to `master`
and the workflow does the rest.

Useful switches: `-Location westeurope`, `-ResourceGroup my-rg`, `-DeployNow` to provision the
infrastructure immediately, `-RotateSecrets` to replace the stored SQL password and JWT key.

The script is idempotent — re-running it reuses whatever already exists.

## What gets created

| Resource | SKU | Cost | What it does |
| --- | --- | --- | --- |
| App Service plan | F1 (Linux) | Free forever | Hosts the API and the built React client |
| Web app | — | Free | `https://gridgame-<hash>.azurewebsites.net` |
| SQL Server | — | Free (logical container) | Hosts the database |
| SQL Database | `GP_S_Gen5_2`, free offer | Free forever | 100,000 vCore-seconds + 32 GB per month |

Resource names carry a suffix derived from the resource group's ID, so deleting the resource group
and re-running the setup produces the same names again.

## Where the secrets live

Nothing sensitive is in the repository or in the Bicep file. Two values are generated once by the
setup script and stored as GitHub Actions secrets:

| Secret | Used for |
| --- | --- |
| `SQL_ADMIN_PASSWORD` | The SQL administrator login, assembled into the connection string by Bicep |
| `JWT_SIGNING_KEY` | `Jwt__Key`, the HMAC key the API signs access tokens with |
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | Identify the app registration to `azure/login` |

The workflow passes the first two into the deployment as `@secure()` parameters, which Azure then
writes into the web app's application settings. They are never printed, never written to the working
tree, and there is no client secret or publish profile anywhere — GitHub proves its identity with a
short-lived OIDC token instead.

Federated credentials exist for two subjects, because the two jobs in the workflow present different
ones: `repo:<owner>/<repo>:ref:refs/heads/master` for the infrastructure job, and
`repo:<owner>/<repo>:environment:production` for the deploy job.

## Free-tier limits worth knowing before you demo it

- **60 CPU-minutes per day.** Once the F1 plan's daily quota is exhausted, the app returns HTTP 403
  until midnight UTC. This is the most likely surprise.
- **No Always On.** After roughly 20 minutes of no traffic the app is unloaded and the next request
  pays a cold start.
- **The database auto-pauses after an hour idle.** Resuming takes 30–60 seconds, so the first request
  after a quiet period is slow. The connection string allows for it, and `Program.cs` retries the
  startup migration rather than crashing.
- **SignalR may not get WebSockets.** WebSockets are not reliably available on Free-tier App Service;
  where they aren't, SignalR negotiates down to Server-Sent Events or long polling. That is fine for a
  turn-based game. If the deployment is rejected outright over the setting, deploy with
  `enableWebSockets=false`.
- **No custom domain with HTTPS.** F1 does not support custom-domain certificates.
- **Ten free databases per subscription.** If that allowance is already spent, deploy with
  `useSqlFreeLimit=false` and accept serverless billing for this one.

## The ARM template, and a "Deploy to Azure" button

[`infra/azuredeploy.json`](infra/azuredeploy.json) is plain ARM JSON **generated** from the Bicep
file. Nothing reads it automatically — it exists so the template can be consumed by tooling that only
speaks ARM, and so it can back a portal button:

`https://portal.azure.com/#create/Microsoft.Template/uri/<url-encoded raw URL of azuredeploy.json>`

`infra/main.bicep` is the source of truth. After editing it, regenerate the JSON or the two will
drift:

```powershell
az bicep build --file infra/main.bicep --outfile infra/azuredeploy.json
```

The button only creates infrastructure. It cannot create the federated credential or set the GitHub
secrets, so `setup-azure.ps1` remains the path that actually gets you to a deployed application in
one step.

## Local development after these changes

Two things moved:

- **The JWT signing key is no longer in `appsettings.Development.json`** (it was committed to a public
  repository). It now comes from user secrets:

  ```powershell
  dotnet user-secrets set "Jwt:Key" "<64 random hex characters>" --project GridGame.Server/GridGameAPI
  ```

  The app fails at startup with that exact instruction if the key is missing.

- **`npm run build` writes into `GridGame.Server/GridGameAPI/wwwroot`** rather than `dist`, so
  `dotnet publish` picks the client up and one App Service instance serves the client, the API and the
  SignalR hub from a single origin. That directory is gitignored. `npm run dev` is unchanged — Vite
  still serves the client and proxies `/Games`, `/Users`, `/Maps` and `/GridGame` to the API.

  One consequence: once you have run `npm run build` at least once, hitting the API's own port
  (`http://localhost:5095`) serves whatever bundle was built last, not what Vite is serving live. Use
  the Vite dev server's port for development; the API port is only interesting for Scalar.

The SignalR hub URL is now relative (`/GridGame`), so it follows whichever origin the app is served
from and no longer hardcodes the local HTTPS port.

## Tearing it down

```powershell
az group delete --name gridgame-rg --yes
```

A deleted free-offer database takes up to an hour to release its slot before another can be created.

## If you outgrow the free tier

In rough order of what tends to bite first:

1. **Azure SignalR Service (free tier)** — 20 concurrent connections and 20,000 messages/day, free
   forever. Clients connect to it directly, which gets you real WebSockets regardless of what the App
   Service plan supports, and offloads connection handling from the 60-minute CPU budget.
2. **Azure Container Apps** — a monthly free grant (180,000 vCPU-seconds, 2M requests), scale-to-zero,
   and WebSocket support. The escape hatch if the F1 CPU quota becomes the binding constraint.
3. **Managed identity instead of the SQL password** — the web app already has a system-assigned
   identity for this. It needs a one-off `CREATE USER ... FROM EXTERNAL PROVIDER`, which is T-SQL and
   therefore cannot live in the Bicep file; that is the only reason it isn't the default here.
4. **Static Web Apps (free) for the client** — free custom domain and certificate, and a CDN. Requires
   splitting the origins, so the API needs a production CORS policy and the client needs a configured
   API base URL.
