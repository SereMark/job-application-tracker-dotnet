using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using JobApplicationTracker.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Features.Applications;

public sealed class GetApplicationSummaryTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CreatedAt = Now.AddDays(-30);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task GetSummaryReturnsZeroCountsForEmptyDatabase()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(Now);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        using HttpClient client = factory.CreateClient();

        ApplicationSummaryResponse summary = await GetSummaryAsync(client, cancellationToken);

        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(
            Enum.GetValues<ApplicationStatus>(),
            summary.StatusCounts.Select(statusCount => statusCount.Status));
        Assert.All(summary.StatusCounts, statusCount => Assert.Equal(0, statusCount.Count));
        Assert.Equal(0, summary.OverdueNextActionCount);
        Assert.Equal(0, summary.NextActionDueWithinSevenDaysCount);
    }

    [Fact]
    public async Task GetSummaryCountsApplicationsByStatus()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(Now);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);

        List<JobApplication> applications = Enum
            .GetValues<ApplicationStatus>()
            .Select(status => CreateApplication(status))
            .Append(CreateApplication(ApplicationStatus.Saved))
            .Append(CreateApplication(ApplicationStatus.Interview))
            .ToList();

        await SeedAsync(factory, applications, cancellationToken);
        using HttpClient client = factory.CreateClient();

        ApplicationSummaryResponse summary = await GetSummaryAsync(client, cancellationToken);
        Dictionary<ApplicationStatus, int> countsByStatus = summary.StatusCounts.ToDictionary(
            statusCount => statusCount.Status,
            statusCount => statusCount.Count);

        Assert.Equal(9, summary.TotalCount);
        Assert.Equal(2, countsByStatus[ApplicationStatus.Saved]);
        Assert.Equal(2, countsByStatus[ApplicationStatus.Interview]);
        Assert.Equal(1, countsByStatus[ApplicationStatus.Applied]);
        Assert.Equal(1, countsByStatus[ApplicationStatus.Screening]);
        Assert.Equal(1, countsByStatus[ApplicationStatus.Offer]);
        Assert.Equal(1, countsByStatus[ApplicationStatus.Rejected]);
        Assert.Equal(1, countsByStatus[ApplicationStatus.Withdrawn]);
    }

    [Fact]
    public async Task GetSummaryUsesExclusiveOverdueAndInclusiveSevenDayBoundaries()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(Now);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);

        JobApplication[] applications =
        [
            CreateApplication(ApplicationStatus.Saved, Now.AddTicks(-1)),
            CreateApplication(ApplicationStatus.Saved, Now),
            CreateApplication(ApplicationStatus.Saved, Now.AddDays(7)),
            CreateApplication(ApplicationStatus.Saved, Now.AddDays(7).AddTicks(1)),
            CreateApplication(ApplicationStatus.Saved),
        ];

        await SeedAsync(factory, applications, cancellationToken);
        using HttpClient client = factory.CreateClient();

        ApplicationSummaryResponse summary = await GetSummaryAsync(client, cancellationToken);

        Assert.Equal(5, summary.TotalCount);
        Assert.Equal(1, summary.OverdueNextActionCount);
        Assert.Equal(2, summary.NextActionDueWithinSevenDaysCount);
    }

    private static JobApplication CreateApplication(
        ApplicationStatus status,
        DateTimeOffset? nextActionDueAt = null) =>
        JobApplication.Create(
            new JobApplicationDetails(
                CompanyName: $"{status} Example Ltd.",
                PositionTitle: ".NET Developer",
                NextActionDescription: nextActionDueAt is null ? null : "Follow up",
                NextActionDueAt: nextActionDueAt),
            status,
            CreatedAt);

    private static async Task SeedAsync(
        JobApplicationTrackerApiFactory factory,
        IEnumerable<JobApplication> applications,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.JobApplications.AddRange(applications);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ApplicationSummaryResponse> GetSummaryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/api/applications/summary",
            cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK but received {(int)response.StatusCode}: {responseJson}");

        ApplicationSummaryResponse? summary =
            JsonSerializer.Deserialize<ApplicationSummaryResponse>(responseJson, JsonOptions);

        return Assert.IsType<ApplicationSummaryResponse>(summary);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter<ApplicationStatus>(
                namingPolicy: null,
                allowIntegerValues: false));

        return options;
    }
}
