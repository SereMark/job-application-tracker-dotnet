using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

string databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(databaseConnectionString));
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "database",
        tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
});

app.Run();
