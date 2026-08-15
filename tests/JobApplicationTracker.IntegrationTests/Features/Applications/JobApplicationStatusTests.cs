using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using JobApplicationTracker.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Features.Applications;

public sealed class JobApplicationStatusTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FirstChangeAt =
        new(2026, 8, 16, 10, 30, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task ChangeStatusUpdatesApplicationAndAddsHistoryEntry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstChangeAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateApplication(ApplicationStatus.Saved);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        var request = new ChangeJobApplicationStatusRequest(
            ApplicationStatus.Screening,
            "  Recruiter call arranged.  ");

        JobApplicationResponse response = await PatchStatusAsync(
            client,
            application.Id,
            request,
            cancellationToken);

        Assert.Equal(ApplicationStatus.Screening, response.Status);
        Assert.Equal(CreatedAt, response.CreatedAt);
        Assert.Equal(FirstChangeAt, response.UpdatedAt);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        List<StatusChange> history = await dbContext.StatusChanges
            .AsNoTracking()
            .OrderBy(change => change.ChangedAt)
            .ThenBy(change => change.Id)
            .ToListAsync(cancellationToken);

        Assert.Equal(ApplicationStatus.Screening, persistedApplication.Status);
        Assert.Equal(FirstChangeAt, persistedApplication.UpdatedAt);
        Assert.Collection(
            history,
            initialChange =>
            {
                Assert.Null(initialChange.PreviousStatus);
                Assert.Equal(ApplicationStatus.Saved, initialChange.NewStatus);
                Assert.Equal(CreatedAt, initialChange.ChangedAt);
                Assert.Null(initialChange.Note);
            },
            latestChange =>
            {
                Assert.Equal(ApplicationStatus.Saved, latestChange.PreviousStatus);
                Assert.Equal(ApplicationStatus.Screening, latestChange.NewStatus);
                Assert.Equal(FirstChangeAt, latestChange.ChangedAt);
                Assert.Equal("Recruiter call arranged.", latestChange.Note);
            });
    }

    [Fact]
    public async Task ChangeToCurrentStatusReturnsConflictWithoutAddingHistory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstChangeAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateApplication(ApplicationStatus.Applied);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        var request = new ChangeJobApplicationStatusRequest(
            ApplicationStatus.Applied,
            "Duplicate status");

        using HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/applications/{application.Id}/status",
            request,
            JsonOptions,
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Job application status conflict", problem.Title);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(ApplicationStatus.Applied, persistedApplication.Status);
        Assert.Equal(CreatedAt, persistedApplication.UpdatedAt);
        Assert.Equal(1, await dbContext.StatusChanges.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ChangeStatusForUnknownApplicationReturnsProblemDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();
        var request = new ChangeJobApplicationStatusRequest(ApplicationStatus.Screening);

        using HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/applications/{unknownId}/status",
            request,
            JsonOptions,
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    public static TheoryData<string> InvalidStatusRequests =>
        new()
        {
            { "{}" },
            { """{"status":"Unknown"}""" },
            { """{"status":1}""" },
            {
                JsonSerializer.Serialize(new
                {
                    status = "Screening",
                    note = new string('x', StatusChange.NoteMaxLength + 1),
                })
            },
        };

    [Theory]
    [MemberData(nameof(InvalidStatusRequests))]
    public async Task ChangeStatusWithInvalidRequestReturnsProblemDetails(string requestJson)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstChangeAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateApplication(ApplicationStatus.Saved);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/applications/{application.Id}/status")
        {
            Content = content,
        };

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(ApplicationStatus.Saved, persistedApplication.Status);
        Assert.Equal(CreatedAt, persistedApplication.UpdatedAt);
        Assert.Equal(1, await dbContext.StatusChanges.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task GetStatusHistoryReturnsChangesInChronologicalOrder()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstChangeAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateApplication(ApplicationStatus.Saved);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();

        await PatchStatusAsync(
            client,
            application.Id,
            new ChangeJobApplicationStatusRequest(
                ApplicationStatus.Applied,
                "Application submitted"),
            cancellationToken);

        timeProvider.Advance(TimeSpan.FromHours(2));

        await PatchStatusAsync(
            client,
            application.Id,
            new ChangeJobApplicationStatusRequest(ApplicationStatus.Interview),
            cancellationToken);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/applications/{application.Id}/status-history",
            cancellationToken);
        List<StatusChangeResponse>? history = await response.Content
            .ReadFromJsonAsync<List<StatusChangeResponse>>(JsonOptions, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(history);
        Assert.Collection(
            history,
            initialChange =>
            {
                Assert.Null(initialChange.PreviousStatus);
                Assert.Equal(ApplicationStatus.Saved, initialChange.NewStatus);
                Assert.Equal(CreatedAt, initialChange.ChangedAt);
            },
            appliedChange =>
            {
                Assert.Equal(ApplicationStatus.Saved, appliedChange.PreviousStatus);
                Assert.Equal(ApplicationStatus.Applied, appliedChange.NewStatus);
                Assert.Equal(FirstChangeAt, appliedChange.ChangedAt);
                Assert.Equal("Application submitted", appliedChange.Note);
            },
            interviewChange =>
            {
                Assert.Equal(ApplicationStatus.Applied, interviewChange.PreviousStatus);
                Assert.Equal(ApplicationStatus.Interview, interviewChange.NewStatus);
                Assert.Equal(FirstChangeAt.AddHours(2), interviewChange.ChangedAt);
                Assert.Null(interviewChange.Note);
            });
    }

    [Fact]
    public async Task GetStatusHistoryForUnknownApplicationReturnsProblemDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/applications/{unknownId}/status-history",
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task FailedHistoryInsertRollsBackCurrentStatusUpdate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstChangeAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateApplication(ApplicationStatus.Saved);
        await SeedAsync(factory, application, cancellationToken);

        await using (AsyncServiceScope setupScope = factory.Services.CreateAsyncScope())
        {
            ApplicationDbContext setupDbContext =
                setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [TR_StatusChanges_ForceFailure]
                ON [StatusChanges]
                INSTEAD OF INSERT
                AS
                BEGIN
                    THROW 51000, 'Forced status history failure.', 1;
                END;
                """,
                cancellationToken);
        }

        using HttpClient client = factory.CreateClient();
        var request = new ChangeJobApplicationStatusRequest(ApplicationStatus.Screening);

        using HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/applications/{application.Id}/status",
            request,
            JsonOptions,
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ApplicationDbContext verificationDbContext =
            verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await verificationDbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(ApplicationStatus.Saved, persistedApplication.Status);
        Assert.Equal(CreatedAt, persistedApplication.UpdatedAt);
        Assert.Equal(
            1,
            await verificationDbContext.StatusChanges.CountAsync(cancellationToken));
    }

    private static JobApplication CreateApplication(ApplicationStatus status) =>
        JobApplication.Create(
            new JobApplicationDetails(
                CompanyName: "Example Ltd.",
                PositionTitle: ".NET Developer"),
            status,
            CreatedAt);

    private static async Task SeedAsync(
        JobApplicationTrackerApiFactory factory,
        JobApplication application,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<JobApplicationResponse> PatchStatusAsync(
        HttpClient client,
        Guid id,
        ChangeJobApplicationStatusRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"/api/applications/{id}/status",
            request,
            JsonOptions,
            cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK but received {(int)response.StatusCode}: {responseJson}");

        JobApplicationResponse? application = JsonSerializer.Deserialize<JobApplicationResponse>(
            responseJson,
            JsonOptions);

        return Assert.IsType<JobApplicationResponse>(application);
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
