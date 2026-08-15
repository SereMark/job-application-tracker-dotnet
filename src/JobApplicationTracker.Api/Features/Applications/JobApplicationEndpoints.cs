using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationTracker.Api.Features.Applications;

internal static partial class JobApplicationEndpoints
{
    private const string GetByIdRouteName = "GetJobApplicationById";
    private const string LoggerCategory =
        "JobApplicationTracker.Api.Features.Applications.JobApplicationEndpoints";

    public static RouteGroupBuilder MapJobApplicationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            .MapGroup("/api/applications")
            .WithTags("Applications");

        group.MapPost("/", CreateAsync)
            .WithName("CreateJobApplication")
            .WithSummary("Create a job application")
            .WithDescription(
                "Creates a job application. Status defaults to Saved when omitted. "
                + "Next action description and due date must be provided together.")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName(GetByIdRouteName)
            .WithSummary("Get a job application")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<CreatedAtRoute<JobApplicationResponse>> CreateAsync(
        CreateJobApplicationRequest request,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        JobApplication application = JobApplication.Create(
            request.ToDetails(),
            request.Status,
            timeProvider.GetUtcNow());

        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync(cancellationToken);

        ILogger logger = loggerFactory.CreateLogger(LoggerCategory);
        LogJobApplicationCreated(
            logger,
            application.Id,
            application.CompanyName,
            application.PositionTitle);

        return TypedResults.CreatedAtRoute(
            application.ToResponse(),
            GetByIdRouteName,
            new { id = application.Id });
    }

    private static async Task<Results<Ok<JobApplicationResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        JobApplication? application = await dbContext.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        if (application is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Job application not found",
                detail: $"No job application with id '{id}' exists.");
        }

        return TypedResults.Ok(application.ToResponse());
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Created job application {JobApplicationId} for {CompanyName} as {PositionTitle}.")]
    private static partial void LogJobApplicationCreated(
        ILogger logger,
        Guid jobApplicationId,
        string companyName,
        string positionTitle);
}
