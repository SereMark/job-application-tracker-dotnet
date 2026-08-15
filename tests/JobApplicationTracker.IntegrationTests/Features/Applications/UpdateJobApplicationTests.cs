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

public sealed class UpdateJobApplicationTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FirstUpdateAt =
        new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task UpdateReplacesEditableDetailsAndIsIdempotent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstUpdateAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateExistingApplication(ApplicationStatus.Applied);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        var request = new UpdateJobApplicationRequest(
            CompanyName: "  Updated Company Ltd.  ",
            PositionTitle: "  Senior .NET Developer  ",
            JobPostingUrl: new Uri("https://example.com/jobs/updated"),
            Source: "  Referral  ",
            Location: "  Budapest  ",
            AppliedOn: new DateOnly(2026, 8, 16),
            Notes: "  Updated notes.  ",
            NextActionDescription: "  Prepare for the interview  ",
            NextActionDueAt: new DateTimeOffset(
                2026,
                8,
                20,
                14,
                0,
                0,
                TimeSpan.FromHours(2)));

        JobApplicationResponse firstResponse = await PutAsync(
            client,
            application.Id,
            request,
            cancellationToken);

        Assert.Equal(application.Id, firstResponse.Id);
        Assert.Equal("Updated Company Ltd.", firstResponse.CompanyName);
        Assert.Equal("Senior .NET Developer", firstResponse.PositionTitle);
        Assert.Equal(new Uri("https://example.com/jobs/updated"), firstResponse.JobPostingUrl);
        Assert.Equal("Referral", firstResponse.Source);
        Assert.Equal("Budapest", firstResponse.Location);
        Assert.Equal(new DateOnly(2026, 8, 16), firstResponse.AppliedOn);
        Assert.Equal("Updated notes.", firstResponse.Notes);
        Assert.Equal("Prepare for the interview", firstResponse.NextActionDescription);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            firstResponse.NextActionDueAt);
        Assert.Equal(ApplicationStatus.Applied, firstResponse.Status);
        Assert.Equal(CreatedAt, firstResponse.CreatedAt);
        Assert.Equal(FirstUpdateAt, firstResponse.UpdatedAt);

        timeProvider.Advance(TimeSpan.FromDays(1));

        JobApplicationResponse repeatedResponse = await PutAsync(
            client,
            application.Id,
            request,
            cancellationToken);

        Assert.Equal(firstResponse, repeatedResponse);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(firstResponse.UpdatedAt, persistedApplication.UpdatedAt);
        Assert.Equal(ApplicationStatus.Applied, persistedApplication.Status);
        Assert.Equal(1, await dbContext.StatusChanges.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task UpdateClearsOmittedOptionalDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstUpdateAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateExistingApplication(ApplicationStatus.Interview);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        var request = new UpdateJobApplicationRequest(
            CompanyName: "Updated Company Ltd.",
            PositionTitle: "Senior .NET Developer");

        JobApplicationResponse response = await PutAsync(
            client,
            application.Id,
            request,
            cancellationToken);

        Assert.Null(response.JobPostingUrl);
        Assert.Null(response.Source);
        Assert.Null(response.Location);
        Assert.Null(response.AppliedOn);
        Assert.Null(response.Notes);
        Assert.Null(response.NextActionDescription);
        Assert.Null(response.NextActionDueAt);
        Assert.Equal(ApplicationStatus.Interview, response.Status);
        Assert.Equal(CreatedAt, response.CreatedAt);
        Assert.Equal(FirstUpdateAt, response.UpdatedAt);
    }

    [Fact]
    public async Task UpdateUnknownJobApplicationReturnsProblemDetails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();
        var request = new UpdateJobApplicationRequest(
            CompanyName: "Example Ltd.",
            PositionTitle: ".NET Developer");

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/applications/{unknownId}",
            request,
            JsonOptions,
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Job application not found", problem.Title);
    }

    public static TheoryData<string> InvalidRequests =>
        new()
        {
            { """{"positionTitle":".NET Developer"}""" },
            { """{"companyName":"Example Ltd.","positionTitle":"   "}""" },
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
                    companyName = "Example Ltd.",
                    positionTitle = ".NET Developer",
                    notes = new string('x', JobApplication.NotesMaxLength + 1),
                })
            },
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task UpdateWithInvalidValuesReturnsValidationProblem(string requestJson)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(FirstUpdateAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        JobApplication application = CreateExistingApplication(ApplicationStatus.Saved);
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PutAsync(
            $"/api/applications/{application.Id}",
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
        JobApplication persistedApplication = await dbContext.JobApplications
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal("Original Company Ltd.", persistedApplication.CompanyName);
        Assert.Equal(CreatedAt, persistedApplication.UpdatedAt);
        Assert.Equal(ApplicationStatus.Saved, persistedApplication.Status);
    }

    private static JobApplication CreateExistingApplication(ApplicationStatus status) =>
        JobApplication.Create(
            new JobApplicationDetails(
                CompanyName: "Original Company Ltd.",
                PositionTitle: ".NET Developer",
                JobPostingUrl: new Uri("https://example.com/jobs/original"),
                Source: "LinkedIn",
                Location: "Remote",
                AppliedOn: new DateOnly(2026, 8, 15),
                Notes: "Original notes.",
                NextActionDescription: "Contact recruiter",
                NextActionDueAt: CreatedAt.AddDays(2)),
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

    private static async Task<JobApplicationResponse> PutAsync(
        HttpClient client,
        Guid id,
        UpdateJobApplicationRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/applications/{id}",
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
