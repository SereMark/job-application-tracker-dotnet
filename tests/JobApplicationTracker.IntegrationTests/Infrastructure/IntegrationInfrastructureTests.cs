using System.Net;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Infrastructure;

public sealed class IntegrationInfrastructureTests(SqlServerContainerFixture sqlServer)
{
    [Fact]
    public async Task FactoryStartsApiAndAppliesMigrations()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEnumerable<string> appliedMigrations =
            await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);

        Assert.Contains("20260815201610_InitialCreate", appliedMigrations);
        Assert.Equal(factory.DatabaseName, dbContext.Database.GetDbConnection().Database);
    }

    [Fact]
    public async Task FactoriesUseSeparateDatabases()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory firstFactory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        await using JobApplicationTrackerApiFactory secondFactory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);

        await using (AsyncServiceScope firstScope = firstFactory.Services.CreateAsyncScope())
        {
            ApplicationDbContext firstDbContext =
                firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            JobApplication application = JobApplication.Create(
                new JobApplicationDetails("Example Ltd.", ".NET Developer"),
                ApplicationStatus.Saved,
                new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero));

            firstDbContext.JobApplications.Add(application);
            await firstDbContext.SaveChangesAsync(cancellationToken);

            Assert.Equal(
                1,
                await firstDbContext.JobApplications.CountAsync(cancellationToken));
            Assert.Equal(
                1,
                await firstDbContext.StatusChanges.CountAsync(cancellationToken));
        }

        await using (AsyncServiceScope secondScope = secondFactory.Services.CreateAsyncScope())
        {
            ApplicationDbContext secondDbContext =
                secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.False(await secondDbContext.JobApplications.AnyAsync(cancellationToken));
            Assert.False(await secondDbContext.StatusChanges.AnyAsync(cancellationToken));
        }

        Assert.NotEqual(firstFactory.DatabaseName, secondFactory.DatabaseName);
    }
}
