using System.Data.Common;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobApplicationTracker.IntegrationTests.Infrastructure;

public sealed class JobApplicationTrackerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    private JobApplicationTrackerApiFactory(string serverConnectionString)
    {
        DatabaseName = $"JobApplicationTrackerTests_{Guid.NewGuid():N}";

        var connectionStringBuilder = new DbConnectionStringBuilder
        {
            ConnectionString = serverConnectionString,
        };

        connectionStringBuilder.Remove("Database");
        connectionStringBuilder.Remove("Initial Catalog");
        connectionStringBuilder["Database"] = DatabaseName;
        _connectionString = connectionStringBuilder.ConnectionString;
    }

    public string DatabaseName { get; }

    public static async Task<JobApplicationTrackerApiFactory> CreateAsync(
        string serverConnectionString,
        CancellationToken cancellationToken = default)
    {
        var factory = new JobApplicationTrackerApiFactory(serverConnectionString);

        try
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            ApplicationDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            return factory;
        }
        catch
        {
            await factory.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");
        builder.ConfigureAppConfiguration(configuration =>
        {
            Dictionary<string, string?> testSettings = new()
            {
                ["ConnectionStrings:Database"] = _connectionString,
            };

            configuration.AddInMemoryCollection(testSettings);
        });
    }
}
