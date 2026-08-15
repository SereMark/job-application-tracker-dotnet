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

public sealed class JobApplicationEndpointsTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CreateThenGetPersistsAndReturnsJobApplication()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new FixedTimeProvider(CreatedAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        using HttpClient client = factory.CreateClient();
        var request = new CreateJobApplicationRequest(
            CompanyName: "  Example Ltd.  ",
            PositionTitle: "  .NET Developer  ",
            Status: ApplicationStatus.Applied,
            JobPostingUrl: new Uri("https://example.com/jobs/42"),
            Source: "  LinkedIn  ",
            Location: "  Budapest  ",
            AppliedOn: new DateOnly(2026, 8, 15),
            Notes: "  Referred by a former colleague.  ",
            NextActionDescription: "  Contact the recruiter  ",
            NextActionDueAt: new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.FromHours(2)));

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/applications",
            request,
            JsonOptions,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("application/json", createResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(createResponse.Headers.Location);

        string createJson = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"status\":\"Applied\"", createJson, StringComparison.Ordinal);
        JobApplicationResponse? created = JsonSerializer.Deserialize<JobApplicationResponse>(
            createJson,
            JsonOptions);

        Assert.NotNull(created);
        Assert.Equal('7', created.Id.ToString("D")[14]);
        Assert.Equal("Example Ltd.", created.CompanyName);
        Assert.Equal(".NET Developer", created.PositionTitle);
        Assert.Equal(new Uri("https://example.com/jobs/42"), created.JobPostingUrl);
        Assert.Equal("LinkedIn", created.Source);
        Assert.Equal("Budapest", created.Location);
        Assert.Equal(new DateOnly(2026, 8, 15), created.AppliedOn);
        Assert.Equal("Referred by a former colleague.", created.Notes);
        Assert.Equal("Contact the recruiter", created.NextActionDescription);
        Assert.Equal(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero), created.NextActionDueAt);
        Assert.Equal(ApplicationStatus.Applied, created.Status);
        Assert.Equal(CreatedAt, created.CreatedAt);
        Assert.Equal(CreatedAt, created.UpdatedAt);
        Assert.EndsWith(
            $"/api/applications/{created.Id}",
            createResponse.Headers.Location.OriginalString,
            StringComparison.Ordinal);

        using HttpResponseMessage getResponse = await client.GetAsync(
            createResponse.Headers.Location,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        JobApplicationResponse? retrieved = await getResponse.Content
            .ReadFromJsonAsync<JobApplicationResponse>(JsonOptions, cancellationToken);

        Assert.Equal(created, retrieved);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        StatusChange initialStatusChange = await dbContext.StatusChanges
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(created.Id, persistedApplication.Id);
        Assert.Equal(created.Status, persistedApplication.Status);
        Assert.Equal(created.Id, initialStatusChange.JobApplicationId);
        Assert.Null(initialStatusChange.PreviousStatus);
        Assert.Equal(ApplicationStatus.Applied, initialStatusChange.NewStatus);
        Assert.Equal(CreatedAt, initialStatusChange.ChangedAt);
        Assert.Null(initialStatusChange.Note);
    }

    [Fact]
    public async Task CreateWithoutStatusDefaultsToSaved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new FixedTimeProvider(CreatedAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent(
            """{"companyName":"Example Ltd.","positionTitle":".NET Developer"}""",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            "/api/applications",
            content,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JobApplicationResponse? created = await response.Content
            .ReadFromJsonAsync<JobApplicationResponse>(JsonOptions, cancellationToken);

        Assert.NotNull(created);
        Assert.Equal(ApplicationStatus.Saved, created.Status);
        Assert.Equal(CreatedAt, created.CreatedAt);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        StatusChange initialStatusChange = await dbContext.StatusChanges
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(ApplicationStatus.Saved, initialStatusChange.NewStatus);
    }

    [Fact]
    public async Task GetUnknownJobApplicationReturnsProblemDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/applications/{unknownId}",
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Job application not found", problem.Title);
    }

    public static TheoryData<string> InvalidValidationRequests =>
        new()
        {
            { """{"positionTitle":".NET Developer"}""" },
            {
                """
                {"companyName":"Example Ltd.","positionTitle":".NET Developer","jobPostingUrl":"/jobs/42"}
                """
            },
            {
                """
                {"companyName":"Example Ltd.","positionTitle":".NET Developer","nextActionDescription":"Contact recruiter"}
                """
            },
            {
                """
                {"companyName":"Example Ltd.","positionTitle":".NET Developer","nextActionDueAt":"2026-08-18T12:00:00Z"}
                """
            },
            {
                JsonSerializer.Serialize(new
                {
                    companyName = new string('x', JobApplication.CompanyNameMaxLength + 1),
                    positionTitle = ".NET Developer",
                })
            },
        };

    [Theory]
    [MemberData(nameof(InvalidValidationRequests))]
    public async Task CreateWithInvalidValuesReturnsValidationProblem(string requestJson)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            "/api/applications",
            content,
            cancellationToken);
        HttpValidationProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.NotEmpty(problem.Errors);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await dbContext.JobApplications.AnyAsync(cancellationToken));
        Assert.False(await dbContext.StatusChanges.AnyAsync(cancellationToken));
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("1")]
    public async Task CreateWithInvalidStatusReturnsProblemDetails(string statusJson)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        string requestJson = $$"""
            {"companyName":"Example Ltd.","positionTitle":".NET Developer","status":{{statusJson}}}
            """;
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(
            "/api/applications",
            content,
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await dbContext.JobApplications.AnyAsync(cancellationToken));
        Assert.False(await dbContext.StatusChanges.AnyAsync(cancellationToken));
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
