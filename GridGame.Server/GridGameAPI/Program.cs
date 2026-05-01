using GridGameAPI.SignalRHubs;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
const string CorsLocalDevPolicyName = "ReactLocalDev";

builder.Services.AddControllers();
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