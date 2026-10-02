using GridGameAPI.ActiveGames;
using GridGameAPI.ActiveGames.SignalRHubs;
using GridGameAPI.Configuration;
using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;

const string CorsLocalDevPolicyName = "ReactLocalDev";

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt")
    );

builder.Services.AddDbContext<GameContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("GridGame")));

builder.Services.AddIdentity<Player, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 12;
}).AddEntityFrameworkStores<GameContext>();

builder.Services.AddSingleton<GameSessionManager>();
builder.Services.AddScoped<JwtManager>();
builder.Services.AddScoped<RefreshTokenManager>();
builder.Services.AddSingleton<GuestCleanup>();
builder.Services.AddHostedService<SessionMonitor>();

//Read eagerly rather than inside the AddJwtBearer callback below, which isn't invoked until the first request that
//needs authentication - a missing key should stop the app at startup, where it is obvious, rather than surfacing as a
//500 on some later request.
//Deliberately not committed to this public repo: locally it comes from user secrets
//(`dotnet user-secrets set "Jwt:Key" "<value>"`), and in Azure from the Jwt__Key app setting that
//infra/main.bicep populates from a GitHub Actions secret
var jwtSigningKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "No JWT signing key is configured. Locally, run: dotnet user-secrets set \"Jwt:Key\" " +
        "\"<64 random hex characters>\" --project GridGame.Server/GridGameAPI");

builder.Services.AddAuthentication(options =>
{
    //Revert the values set by Identity
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    //JwtManager doesn't stamp an issuer/audience, and claims are emitted under their ClaimTypes names already, so no inbound remapping
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        ValidateIssuer = false,
        ValidateAudience = false,
    };
    options.Events = new JwtBearerEvents
    {
        //Browsers can't set headers on a SignalR websocket handshake, so the token arrives as a query string parameter instead
        OnMessageReceived = context =>
        {
            if (context.Request.Query.TryGetValue("access_token", out var queryStringToken))
            {
                context.Token = queryStringToken;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicy.AnyUser, policy => policy.RequireAuthenticatedUser())
    .AddPolicy(AuthPolicy.AdminOnly, policy => policy.RequireRole(AppRole.Admin))
    .AddPolicy(AuthPolicy.NamedAccountOnly, policy => policy.RequireClaim(Constants.AccountTypeClaim, Constants.NamedAccountType));

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new TwoDimensionalIntArrayJsonConverter()));

var signalRBuilder = builder.Services.AddSignalR();
//In Azure, client connections are held by Azure SignalR Service rather than by this process: the free App Service
//plan can't be relied on for inbound WebSockets, and the service keeps connection handling off the plan's daily CPU
//quota. Keyed off the connection string's presence so local development keeps the plain in-process hub. The hub
//code is unchanged either way - the SDK carries the negotiating user's claims through, so Context.User and
//Context.UserIdentifier behave the same
if (!string.IsNullOrEmpty(builder.Configuration["Azure:SignalR:ConnectionString"]))
{
    signalRBuilder.AddAzureSignalR();
}

builder.Services.AddOpenApi();

builder.Services.AddCors(options => options.AddPolicy(CorsLocalDevPolicyName,
    policy => policy.SetIsOriginAllowed(origin => true) 
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod()));

//-------------------------------------------------
// Configure the HTTP request pipeline.
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    //Must run before UseAuthorization, otherwise CORS preflights against [Authorize] endpoints are rejected with a 401 that carries no CORS headers
    app.UseCors(CorsLocalDevPolicyName);
}
else
{
    //Skipped in development: the Vite proxy talks to the http port, and browsers drop the Authorization header when following the redirect to the https one.
    //In Azure this relies on the ASPNETCORE_FORWARDEDHEADERS_ENABLED app setting - App Service terminates TLS at
    //the front end and forwards to the worker over plain HTTP, so without the forwarded headers every request would
    //look insecure and redirect to itself forever
    app.UseHttpsRedirection();
}

//The React bundle is published into wwwroot (see the client's vite.config.ts), so in Azure the SPA, the API and the
//SignalR hub all share one origin and no production CORS policy is needed. In development this serves nothing, because
//Vite serves the client itself
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var hubConfigurator = app.MapHub<GridGameHub>("/GridGame");
if (app.Environment.IsDevelopment())
{
    hubConfigurator.RequireCors(CorsLocalDevPolicyName);
}

//Pinged by the client while a game is open. With Azure SignalR, gameplay traffic reaches this app over its own outbound
//connection to the service, so none of it counts as an incoming request - and without Always On (unavailable on the
//free plan) App Service unloads an app after ~20 minutes of no incoming requests, mid-game included
app.MapGet("/healthz", () => Results.Ok());

//React Router owns the client-side routes, so any unmatched GET that isn't an API route or a real file has to be
//answered with the SPA shell rather than a 404 - otherwise deep links and refreshes break
app.MapFallbackToFile("index.html");

using (var serviceScope = app.Services.CreateScope())
{
    //ServiceLocator pattern isn't ideal, but this seems to be the best option currently to ensure the database is migrated and seeded on startup
    var dbContext = serviceScope.ServiceProvider.GetRequiredService<GameContext>();
    await MigrateWithRetryAsync(dbContext, app.Logger);

    if (app.Environment.IsDevelopment())
    {
        //Catches the case where the model was changed but `dotnet ef migrations add` was never run for it
        if (dbContext.Database.HasPendingModelChanges())
        {
            throw new InvalidOperationException(
                "The EF Core model has changes that aren't captured in a migration. " +
                "Run `dotnet ef migrations add <Name>` (from the repo root: `dotnet tool run dotnet-ef migrations add <Name> --project GridGame.Server/GridGameAPI --startup-project GridGame.Server/GridGameAPI -o Database/Migrations`) before starting the app.");
        }

        //Deliberately development-only: a deployed environment starts empty and is populated by real registrations
        if (!await dbContext.GridGames.AnyAsync())
        {
            dbContext.GridGames.Add(GameContext.SeedData());
            await dbContext.SaveChangesAsync();
        }
    }

    serviceScope.ServiceProvider.GetRequiredService<GuestCleanup>().TriggerIfDue();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();

//Azure SQL's serverless tier auto-pauses after an hour idle, and the request that wakes it up is rejected while the
//resume completes - roughly a minute. Without the retry, a deploy onto a cold database crashes the app on startup
static async Task MigrateWithRetryAsync(GameContext dbContext, ILogger logger)
{
    //Bounded so the whole loop stays well inside App Service's container start timeout
    const int maxAttempts = 6;
    var delay = TimeSpan.FromSeconds(5);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await dbContext.Database.MigrateAsync();
            return;
        }
        catch (Exception exception) when (attempt < maxAttempts)
        {
            logger.LogWarning(exception,
                "Migration attempt {Attempt} of {MaxAttempts} failed; the database may still be resuming. Retrying in {Delay}.",
                attempt, maxAttempts, delay);
            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 20));
        }
    }
}