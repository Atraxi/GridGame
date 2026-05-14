using GridGameAPI.ActiveGames;
using GridGameAPI.ActiveGames.SignalRHubs;
using GridGameAPI.Database;
using GridGameAPI.UtilityInfrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
const string CorsLocalDevPolicyName = "ReactLocalDev";

builder.Services.AddSingleton<GameSessionManager>();
builder.Services.AddDbContext<GameContext>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new TwoDimensionalIntArrayJsonConverter());
    });
builder.Services.AddSignalR();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsLocalDevPolicyName, policy =>
    {
        policy.SetIsOriginAllowed(origin => true) //new Uri(origin).IsLoopback)
        //policy.AllowAnyOrigin()
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    using (var serviceScope = app.Services.CreateScope())
    {
        //ServiceLocator pattern isn't ideal, but for an in-memory database this seems to be the best option currently to ensure it's initialialised and seeded
        var dbContext = serviceScope.ServiceProvider.GetRequiredService<GameContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseCors(CorsLocalDevPolicyName);
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
var hubConfigurator = app.MapHub<GridGameHub>("/GridGame");
if (app.Environment.IsDevelopment())
{
    hubConfigurator.RequireCors(CorsLocalDevPolicyName);
}

app.Run();