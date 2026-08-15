using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using JobApplicationTracker.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Features.Applications;

public sealed class GetJobApplicationsTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task GetAllUsesDefaultSortAndPagination()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);

        IEnumerable<JobApplication> applications = Enumerable.Range(0, 22)
            .Select(index => CreateApplication(
                companyName: $"Company {index:D2}",
                positionTitle: ".NET Developer",
                createdAt: BaseTime.AddHours(index)));

        await SeedAsync(factory, applications, cancellationToken);

        using HttpClient client = factory.CreateClient();
        PagedJobApplicationsResponse firstPage = await GetPageAsync(
            client,
            "/api/applications",
            cancellationToken);

        Assert.Equal(1, firstPage.Page);
        Assert.Equal(GetJobApplicationsQuery.DefaultPageSize, firstPage.PageSize);
        Assert.Equal(22, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal("Company 21", firstPage.Items[0].CompanyName);
        Assert.Equal("Company 02", firstPage.Items[^1].CompanyName);
        Assert.True(firstPage.Items
            .Zip(firstPage.Items.Skip(1))
            .All(pair => pair.First.UpdatedAt > pair.Second.UpdatedAt));

        PagedJobApplicationsResponse secondPage = await GetPageAsync(
            client,
            "/api/applications?page=2",
            cancellationToken);

        Assert.Equal(2, secondPage.Page);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Collection(
            secondPage.Items,
            application => Assert.Equal("Company 01", application.CompanyName),
            application => Assert.Equal("Company 00", application.CompanyName));
    }

    [Fact]
    public async Task GetAllCombinesSearchAndFilters()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);

        JobApplication expected = CreateApplication(
            companyName: "Acme Cloud",
            positionTitle: "Platform Engineer",
            createdAt: BaseTime,
            status: ApplicationStatus.Applied,
            source: "LinkedIn",
            appliedOn: new DateOnly(2026, 8, 10),
            nextActionDueAt: new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

        JobApplication[] applications =
        [
            expected,
            CreateApplication(
                "Late Action Ltd.",
                "Platform Engineer",
                BaseTime.AddHours(1),
                ApplicationStatus.Applied,
                "LinkedIn",
                new DateOnly(2026, 8, 10),
                new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)),
            CreateApplication(
                "Old Application Ltd.",
                "Platform Engineer",
                BaseTime.AddHours(2),
                ApplicationStatus.Applied,
                "LinkedIn",
                new DateOnly(2026, 8, 1),
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
            CreateApplication(
                "Different Status Ltd.",
                "Platform Engineer",
                BaseTime.AddHours(3),
                ApplicationStatus.Interview,
                "LinkedIn",
                new DateOnly(2026, 8, 10),
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
            CreateApplication(
                "Different Source Ltd.",
                "Platform Engineer",
                BaseTime.AddHours(4),
                ApplicationStatus.Applied,
                "Company website",
                new DateOnly(2026, 8, 10),
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
            CreateApplication(
                "Different Role Ltd.",
                "Product Designer",
                BaseTime.AddHours(5),
                ApplicationStatus.Applied,
                "LinkedIn",
                new DateOnly(2026, 8, 10),
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
        ];

        await SeedAsync(factory, applications, cancellationToken);

        using HttpClient client = factory.CreateClient();
        PagedJobApplicationsResponse page = await GetPageAsync(
            client,
            "/api/applications?search=engineer&status=applied&source=LinkedIn"
                + "&appliedFrom=2026-08-05&appliedTo=2026-08-15"
                + "&nextActionBefore=2026-08-21T00%3A00%3A00%2B02%3A00",
            cancellationToken);

        JobApplicationResponse result = Assert.Single(page.Items);
        Assert.Equal(expected.Id, result.Id);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task GetAllSortsByAllowedFieldAndDirection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);

        JobApplication[] applications =
        [
            CreateApplication("Charlie Ltd.", ".NET Developer", BaseTime),
            CreateApplication("Alpha Ltd.", ".NET Developer", BaseTime.AddHours(1)),
            CreateApplication("Bravo Ltd.", ".NET Developer", BaseTime.AddHours(2)),
        ];

        await SeedAsync(factory, applications, cancellationToken);

        using HttpClient client = factory.CreateClient();
        PagedJobApplicationsResponse page = await GetPageAsync(
            client,
            "/api/applications?sortBy=companyName&sortDirection=asc",
            cancellationToken);

        Assert.Equal(
            ["Alpha Ltd.", "Bravo Ltd.", "Charlie Ltd."],
            page.Items.Select(application => application.CompanyName));
    }

    public static TheoryData<string> InvalidQueries =>
        new()
        {
            { "?page=0" },
            { "?pageSize=101" },
            { "?page=2147483647&pageSize=100" },
            { "?appliedFrom=2026-08-20&appliedTo=2026-08-10" },
            { "?appliedFrom=not-a-date" },
            { "?status=Unknown" },
            { "?status=999" },
            { "?sortBy=NotAllowed" },
            { "?sortDirection=Sideways" },
        };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task GetAllWithInvalidQueryReturnsProblemDetails(string queryString)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/applications{queryString}",
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    private static JobApplication CreateApplication(
        string companyName,
        string positionTitle,
        DateTimeOffset createdAt,
        ApplicationStatus status = ApplicationStatus.Saved,
        string? source = null,
        DateOnly? appliedOn = null,
        DateTimeOffset? nextActionDueAt = null) =>
        JobApplication.Create(
            new JobApplicationDetails(
                companyName,
                positionTitle,
                Source: source,
                AppliedOn: appliedOn,
                NextActionDescription: nextActionDueAt is null ? null : "Follow up",
                NextActionDueAt: nextActionDueAt),
            status,
            createdAt);

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

    private static async Task<PagedJobApplicationsResponse> GetPageAsync(
        HttpClient client,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK but received {(int)response.StatusCode}: {responseJson}");

        PagedJobApplicationsResponse? page = JsonSerializer.Deserialize<PagedJobApplicationsResponse>(
            responseJson,
            JsonOptions);

        return Assert.IsType<PagedJobApplicationsResponse>(page);
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
