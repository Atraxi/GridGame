# Deploying GridGame to Azure

Everything runs on free-tier Azure resources, and no secret is ever committed.

[`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) builds the client and API on every push
to `master` and ships them to an existing web app. Infrastructure is created by hand in the portal,
so nothing is created, resized or billed without you seeing it. The workflow never touches Azure
resources, and its identity can be limited to deploying that one web app.

Nothing here needs the Azure CLI or GitHub CLI installed locally.

## Before the first deploy

- **Rotate anything that was ever committed.** The JWT signing key that used to live in
  `appsettings.Development.json` is in the public git history; it is dev-only and is no longer used,
  but don't reuse that value anywhere.
- **`Microsoft.OpenApi` 2.0.0 carries a known high-severity advisory** (pulled in transitively by
  Scalar; `NU1903` at build time). Only the Scalar/OpenAPI endpoints are affected and they are
  development-only, but it is worth clearing before putting the app on the public internet.

## Provisioning by hand

### What to create

| Resource | SKU | Cost | What it does |
| --- | --- | --- | --- |
| Resource group | — | Free | Holds everything below |
| App Service plan | F1 (Linux) | Free forever | Hosts the API and the built React client |
| Web app | — | Free | `https://<name>.azurewebsites.net` |
| SQL Server | — | Free (logical container) | Hosts the database |
| SQL Database | `GP_S_Gen5_2`, free offer | Free forever | 100,000 vCore-seconds + 32 GB per month |
| SignalR Service | `Free_F1` | Free forever | Holds browsers' real-time connections. Optional — see [Azure SignalR Service](#azure-signalr-service) |

1. **Resource group.** *Resource groups* → *Create*, e.g. `gridgame-rg`. Use one region for
   everything, and pick it by where the **free SQL offer** is available — not every region has it.
   New Zealand North rejects it with `ProvisioningDisabled` (*not supported for provided service level
   objective or region*); Australia East is the nearest region to try instead. A database must live in
   its server's region, so a server created in the wrong region can't be reused — delete it (an empty
   server costs nothing) and create a new one.
2. **Web app.** *Create a resource* → *Web App*. Publish **Code**, runtime stack **.NET 10 (LTS)**,
   operating system **Linux**, and a new App Service plan on **Free F1**. Either setting of *unique
   default hostname* is fine; the workflow asks Azure for the real hostname rather than guessing it.

   Then, under *Settings* → *Configuration* → *General settings*: **HTTPS only** on, **Web sockets**
   on, **FTP state** disabled, **Minimum TLS** 1.2, **HTTP version** 2.0. (*Always On* isn't
   available on F1.)
3. **SQL.** *Create a resource* → *SQL Database*. Accept the **free database offer** banner, which
   sets the serverless General Purpose shape the offer requires. Create a new server with **SQL
   authentication**, and set *Behavior when free limit reached* to **auto-pause until next month** —
   the alternative is to start billing. Name the database `GridGame`. Under *Networking*, use a public
   endpoint and set **Allow Azure services and resources to access this server** to yes.
4. **SignalR** (optional). *Create a resource* → *SignalR Service*, pricing tier **Free**, service mode
   **Default**. Copy the connection string from *Keys*.
5. **App settings.** Web app → *Settings* → *Environment variables* → *App settings*:

   | Name | Value |
   | --- | --- |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` — App Service terminates TLS, and without this the HTTPS redirect loops |
   | `ConnectionStrings__GridGame` | See below |
   | `Jwt__Key` | 64 random hex characters. A password manager's generator is the easy way. |
   | `SCM_DO_BUILD_DURING_DEPLOYMENT` | `false` — CI ships an already-built app |
   | `WEBSITES_CONTAINER_START_TIME_LIMIT` | `600` — the startup migration can wait on a paused database |
   | `Azure__SignalR__ConnectionString` | From step 4. Leave it out to have the web app hold connections itself. |

   `Jwt__AccessTokenExpiryMinutes` (default 60) and `Jwt__RefreshTokenExpiryDays` (default 180) are
   optional.

   Use this connection string. The long timeout and retries are what turn a paused database's
   30–60-second resume into a slow first request rather than an error:

   ```text
   Server=tcp:<server>.database.windows.net,1433;Initial Catalog=GridGame;Persist Security Info=False;User ID=<admin login>;Password=<admin password>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;ConnectRetryCount=12;ConnectRetryInterval=10;
   ```

   Avoid `; ' " = { }` in the SQL password, since it sits inside this string.

### Keeping costs at zero

Azure has no hard spending cap on a pay-as-you-go subscription. Instead, there are several layers,
roughly from most to least effective.

**The free SKUs cap themselves.** Each resource above fails closed rather than billing. F1 returns 403
once its CPU quota is spent, the SignalR Free tier refuses messages past its daily limit, and the free
database pauses when its monthly allowance runs out. What these settings guard against is
something *else* being created or resized, which is what the remaining layers cover.

**Block anything else from being created.** Assign the built-in Azure Policy **Allowed resource
types** at the subscription or resource group, listing only `Microsoft.Web/serverFarms`,
`Microsoft.Web/sites`, `Microsoft.Sql/servers`, `Microsoft.Sql/servers/databases` and
`Microsoft.SignalRService/SignalR`. Creation of anything else is then denied, by anyone. Policy itself is
free. It doesn't stop a permitted resource being moved to a paid SKU; that needs a small custom policy
on the SKU field. Try a harmless change after assigning it, in case it also needs some child resource
types allowed.

**A budget alert.** *Cost Management* → *Budgets* → *Add*: a budget of `1`, with an email alert at
100% of actual cost. It only notifies, and cost data lags by hours, so treat it as the tripwire that
tells you the layers above were bypassed.

**An automated kill switch.** A budget can trigger an *action group* instead of just an email, and that
can run an Automation runbook, Logic App or Function to stop the web app or delete the resource group.
That limits the damage; it is not a cap, because of the same reporting lag.

**A true hard cap** exists only on credit-based subscriptions: the free account's trial credit, Azure
for Students, and Visual Studio subscriber credits. Those have a *spending limit* that disables the
subscription when the credit runs out rather than billing. Visual Studio credits are licensed for
dev/test only, so they don't fit a public game.

## Letting GitHub deploy

GitHub signs in to Azure with a short-lived OIDC token, so there is no password, publish profile or
client secret anywhere. Set it up once, in the portal and on the GitHub website:

1. **Identity.** *Microsoft Entra ID* → *App registrations* → *New registration*. Any name (e.g.
   `github-gridgame`), single tenant, no redirect URI. From its overview page, note the
   **Application (client) ID** and **Directory (tenant) ID**.
2. **Trust.** In that app registration: *Certificates & secrets* → *Federated credentials* → *Add
   credential* → scenario **GitHub Actions deploying Azure resources**. Enter your GitHub account and
   repository name, entity type **Environment**, environment `production`.
3. **Access.** Open the **web app** (not the resource group) → *Access control (IAM)* → *Add role
   assignment* → **Website Contributor** → *Members* → select the app registration by name. That lets
   it deploy to and read this one app, and nothing else, so CI can never create or resize anything.

   Two things to check on the first run, since this narrower setup hasn't been exercised yet:
   - If *Sign in to Azure* fails because it can't find the subscription, or a later step fails with
     an authorization error, grant **Contributor on the resource group** instead.
   - If the deploy step fails with a 401, look at the web app's *Configuration* → *General settings*
     → **SCM Basic Auth Publishing Credentials**, which the portal turns off by default.
4. **Subscription ID.** *Subscriptions* → copy the ID of the one holding the resource group.
5. **GitHub.** Repository → *Settings* → *Secrets and variables* → *Actions*:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `AZURE_CLIENT_ID` | Application (client) ID from step 1 |
   | Secret | `AZURE_TENANT_ID` | Directory (tenant) ID from step 1 |
   | Secret | `AZURE_SUBSCRIPTION_ID` | From step 4 |
   | Variable | `AZURE_RESOURCE_GROUP` | e.g. `gridgame-rg` |
   | Variable | `AZURE_WEBAPP_NAME` | The web app's name, without `.azurewebsites.net` |

   The three IDs aren't truly secret, but keeping them as secrets keeps them out of logs.
6. **Deploy.** *Actions* tab → *Deploy to Azure* → *Run workflow*.

The SQL password, JWT key and SignalR key live only in the web app's settings. They never pass through
GitHub.

## Azure SignalR Service

In Azure, browsers hold their real-time connection with Azure SignalR Service rather than with the web
app. The hub code is unchanged: the browser still negotiates with the web app (which checks its JWT as
before), gets redirected to the service, and the service relays hub calls to the web app over a few
outbound connections the app keeps open. `Context.User` and `Context.UserIdentifier` carry through.

**Why:** it gives real WebSockets on the free App Service plan, which can't be relied on for them, and
it keeps connection handling off the plan's daily CPU quota.

**Local development is unaffected.** The app only switches to Azure SignalR when
`Azure:SignalR:ConnectionString` is configured, and only Azure sets it. Locally you get the plain
in-process hub and debug it exactly as before. If you ever want to debug against the real service, put
its connection string in user secrets. The app dials out to the service, so this works from localhost
with no tunnel.

**Drawbacks:**

- **20 concurrent connections**, hard ceiling. Every open game tab counts, spectators included.
- **20,000 outbound messages per day**, and the Free tier has no overage: beyond that the service
  refuses messages until the next UTC day rather than billing you. Messages are counted in 2 KB units.
  Every move broadcasts to every connection in the game, so a 2-player game costs about 2–4 messages
  per move — thousands of moves a day of headroom.
- **Games would stop keeping the app awake.** With the service in place, moves reach the app over its
  own outbound connection, so they don't count as incoming requests — and without Always On, App
  Service unloads an app after ~20 minutes without one. The client therefore pings `/healthz` every 5
  minutes while a game page is open.
- **One more hop to debug.** A failure could now be between the app and the service rather than the
  browser and the app. The service's *Live Trace* blade in the portal is where to look.
- **One more key** exists (in an app setting) that the in-process setup didn't need.

Delete the `Azure__SignalR__ConnectionString` app setting to switch back without code changes; the free
resource can stay, unused.

## Free-tier limits worth knowing before you demo it

- **60 CPU-minutes per day.** Once the F1 plan's daily quota is exhausted, the app returns HTTP 403
  until midnight UTC. This is the most likely surprise.
- **No Always On.** After ~20 minutes with no incoming requests the app is unloaded, and the next
  request pays a cold start.
- **The database auto-pauses after an hour idle.** Resuming takes 30–60 seconds, so the first request
  after a quiet period is slow. The connection string allows for it, and `Program.cs` retries the
  startup migration rather than crashing.
- **F1 can't bind a custom domain itself** — the Cloudflare proxy in the next section works around it.
- **Ten free databases per subscription.** If that allowance is already spent, the portal won't offer
  the free banner, and this database would be billed.

## Custom domains

F1 cannot bind a custom domain itself: App Service routes by the `Host` header and F1 only answers to
`*.azurewebsites.net`. A plain DNS record or proxied CNAME therefore doesn't work, but a proxy that
rewrites `Host` does. Your options:

| Route | Cost | What changes |
| --- | --- | --- |
| **Cloudflare Worker proxy** | free | Nothing in the app. Needs the domain's DNS on Cloudflare — see below. |
| **B1 App Service** | about $13/month (varies by region) | Scale the plan up to B1. You also get Always On, no CPU quota, and WebSockets, so dropping Azure SignalR becomes reasonable. |
| **Container Apps** | free to a few dollars a month | A rework of the hosting, with real constraints — see below |
| **Static Web Apps for the client** | free | Custom domain for the client only; the API stays on `azurewebsites.net`. Splits the origins, so it needs a production CORS policy and a configured API base URL — undoing the single-origin setup. |

### Cloudflare Worker proxy (free, stays on F1)

[`cloudflare/proxy`](cloudflare/proxy) is a small Worker that re-issues every request against the web
app's `azurewebsites.net` name, so App Service sees the `Host` it expects while the browser only ever
sees your domain. The SPA, the API and the SignalR negotiate call stay on one origin, so the app needs
no CORS policy or configuration change. Auth is a bearer header rather than a cookie, so nothing is
tied to the hostname. With Azure SignalR on, the WebSocket itself goes straight from the browser to
the SignalR service and never touches the Worker.

The workflow's `proxy` job deploys the Worker on every push and passes it the web app's hostname, as
reported by Azure in the deploy job. The job is skipped until `CLOUDFLARE_ACCOUNT_ID` exists. The Cloudflare side is set
up by hand, once:

1. **API token.** Cloudflare dashboard → *My Profile* → *API Tokens* → *Create Token* → template
   **Edit Cloudflare Workers**. Scope it to your account.
2. **GitHub.** Repository → *Settings* → *Secrets and variables* → *Actions*:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `CLOUDFLARE_API_TOKEN` | The token from step 1 |
   | Variable | `CLOUDFLARE_ACCOUNT_ID` | Dashboard → *Workers & Pages* → *Account ID* |

3. **Deploy.** *Actions* → *Deploy to Azure* → *Run workflow*. This creates the `gridgame-proxy`
   Worker; check it at its `workers.dev` URL before going further.
4. **Domain.** *Workers & Pages* → `gridgame-proxy` → *Settings* → *Domains & Routes* → *Add* →
   *Custom Domain*, and enter the hostname. Cloudflare creates the DNS record and certificate itself.
   No routes are declared in `wrangler.jsonc`, so later deploys should leave the domain attached —
   confirm that after the second deploy.

To deploy the Worker by hand instead: `npx wrangler deploy --var ORIGIN_HOST:<site>.azurewebsites.net`
from `cloudflare/proxy`. To try it locally, `npx wrangler dev` with the same `--var`.

What the proxy does not change:

- Every F1 limit above still applies: the CPU quota, the idle unload and the database auto-pause.
- The `azurewebsites.net` URL keeps working alongside the domain.
- Workers' free plan allows 100,000 requests a day and 10 ms of CPU per request. The proxy does no
  work of its own, so only the request count matters.

### B1

With B1, binding the domain is a one-time portal step: web app → *Custom domains* → *Add custom
domain* → *App Service Managed Certificate* (free), then add the CNAME and TXT records it shows you at
your DNS provider. Redeploys won't remove it, because shipping code doesn't touch domains.

## Moving to Azure Container Apps: the implications

Container Apps is the other way to get a custom domain (with a free managed certificate), real
WebSockets, and no daily CPU cliff. But it doesn't suit this app as it stands:

- **Only free if it scales to zero.** The monthly free grant (180,000 vCPU-seconds, 360,000
  GiB-seconds) covers about 200 hours a month of one running replica at the smallest size (0.25 vCPU /
  0.5 GiB). An always-on replica uses roughly 3.6 times that, so a minimum of one replica means a few
  dollars a month.
- **Exactly one replica, ever.** `GameSessionManager` keeps live games in process memory. Two
  replicas would each build their own copy of a game, and each would notify only its own half of the
  players — silently. `maxReplicas: 1` is mandatory until sessions move to a shared store.
- **Scale-to-zero and Azure SignalR don't mix.** Scaling is driven by incoming requests, and with the
  service in place gameplay doesn't produce any. The app would scale to zero mid-game and drop its
  connections to the service. On Container Apps you would turn Azure SignalR off and use native
  WebSockets instead. An open WebSocket counts as an in-flight request, which *should* hold the replica
  up for as long as anyone is connected. That needs verifying before relying on it.
- **Scale-in discards in-memory state.** Games persist once per round, so a replica stopping mid-round
  loses up to a round of moves. In practice it only scales in once nobody is connected, so this should
  rarely bite.
- **Cold starts stack.** From zero, the first visitor waits for the container to start (roughly 5–15
  seconds), plus 30–60 seconds more if the database has also paused.
- **More moving parts.** It needs a container image: `dotnet publish /t:PublishContainer` builds one
  without a Dockerfile, and GitHub Container Registry is free for public repositories. The workflow
  pushes the image and updates the app, which replaces the plan and web app. Logs are live-stream only unless you add Log Analytics, which isn't free.

**In short:** B1 is the no-surprises route to a custom domain for about $13 a month. Container Apps
can probably keep you at or near $0 with a domain, but it needs the single-replica constraint and
verification that WebSockets keep it awake. Choose it if staying free matters more than simplicity.

## Local development after these changes

- **The JWT signing key is no longer in `appsettings.Development.json`** (it was committed to a public
  repository). It now comes from user secrets:

  ```powershell
  dotnet user-secrets set "Jwt:Key" "<64 random hex characters>" --project GridGame.Server/GridGameAPI
  ```

  The app fails at startup with that exact instruction if the key is missing.

- **`npm run build` writes into `GridGame.Server/GridGameAPI/wwwroot`** rather than `dist`, so
  `dotnet publish` picks the client up and one web app serves the client, the API and the SignalR hub
  from a single origin. That directory is gitignored. `npm run dev` is unchanged — Vite still serves
  the client and proxies the API routes, now including `/GridGame` (WebSockets included) and
  `/healthz`.

  One consequence: once you have run `npm run build` at least once, hitting the API's own port
  (`http://localhost:5095`) serves whatever bundle was built last, not what Vite is serving live. Use
  the Vite dev server's port for development; the API port is only interesting for Scalar.

- **The SignalR hub URL is now relative** (`/GridGame`), so it follows whichever origin the app is
  served from and no longer hardcodes the local HTTPS port.

## Tearing it down

Portal: open the resource group → *Delete resource group*.

A deleted free-offer database takes up to an hour to release its slot before another can be created.
The app registration lives outside the resource group; delete it from *App registrations* if you're
done for good.

## Further hardening

- **Managed identity instead of the SQL password.** Turn on the web app's system-assigned identity
  (*Identity* blade), run a one-off `CREATE USER ... FROM EXTERNAL PROVIDER` for it in the database,
  and drop the password from the connection string in favour of `Authentication=Active Directory Managed Identity`.
- **Managed identity for SignalR**, likewise. Give the web app's identity the **SignalR App Server**
  role on the service, and use a connection string of the form
  `Endpoint=https://<name>.service.signalr.net;AuthType=azure.msi;Version=1.0;`.
