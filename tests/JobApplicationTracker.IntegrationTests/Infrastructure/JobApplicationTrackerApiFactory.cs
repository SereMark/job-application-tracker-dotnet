using System.Data.Common;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobApplicationTracker.IntegrationTests.Infrastructure;

public sealed class JobApplicationTrackerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly TimeProvider? _timeProvider;

    private JobApplicationTrackerApiFactory(
        string serverConnectionString,
        TimeProvider? timeProvider)
    {
        _timeProvider = timeProvider;
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
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var factory = new JobApplicationTrackerApiFactory(serverConnectionString, timeProvider);

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
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration(configuration =>
        {
            Dictionary<string, string?> testSettings = new()
            {
                ["ConnectionStrings:Database"] = _connectionString,
            };

            configuration.AddInMemoryCollection(testSettings);
        });

        if (_timeProvider is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            });
        }
    }
}
