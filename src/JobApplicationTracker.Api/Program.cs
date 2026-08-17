using System.Text.Json.Serialization;
using JobApplicationTracker.Api.Features.Applications;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.Configure<RouteHandlerOptions>(static options =>
{
    options.ThrowOnBadRequest = false;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ApplicationResume.MaxFileSize + (64 * 1_024);
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter<ApplicationStatus>(
            namingPolicy: null,
            allowIntegerValues: false));
});
builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string databaseConnectionString = configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

    options.UseSqlServer(databaseConnectionString);
});
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "database",
        tags: ["ready"]);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ApplicationDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapJobApplicationEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
});

app.Run();
