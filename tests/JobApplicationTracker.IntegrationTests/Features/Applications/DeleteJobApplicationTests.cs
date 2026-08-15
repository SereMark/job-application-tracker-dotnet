using System.Net;
using System.Net.Http.Json;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using JobApplicationTracker.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Features.Applications;

public sealed class DeleteJobApplicationTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteExistingApplicationReturnsNoContentAndCascadesHistory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        JobApplication application = CreateApplication();
        application.ChangeStatus(
            ApplicationStatus.Applied,
            "Application submitted",
            CreatedAt.AddHours(1));
        application.ChangeStatus(
            ApplicationStatus.Interview,
            "Interview arranged",
            CreatedAt.AddHours(2));
        await SeedAsync(factory, application, cancellationToken);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.DeleteAsync(
            $"/api/applications/{application.Id}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await dbContext.JobApplications
            .AnyAsync(item => item.Id == application.Id, cancellationToken));
        Assert.False(await dbContext.StatusChanges
            .AnyAsync(change => change.JobApplicationId == application.Id, cancellationToken));
    }

    [Fact]
    public async Task DeleteUnknownApplicationReturnsProblemDetailsWithoutDeletingOtherData()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        JobApplication existingApplication = CreateApplication();
        await SeedAsync(factory, existingApplication, cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.DeleteAsync(
            $"/api/applications/{unknownId}",
            cancellationToken);
        ProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Job application not found", problem.Title);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(await dbContext.JobApplications
            .AnyAsync(item => item.Id == existingApplication.Id, cancellationToken));
        Assert.Equal(
            1,
            await dbContext.StatusChanges.CountAsync(cancellationToken));
    }

    private static JobApplication CreateApplication() =>
        JobApplication.Create(
            new JobApplicationDetails(
                CompanyName: "Example Ltd.",
                PositionTitle: ".NET Developer"),
            ApplicationStatus.Saved,
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
}
