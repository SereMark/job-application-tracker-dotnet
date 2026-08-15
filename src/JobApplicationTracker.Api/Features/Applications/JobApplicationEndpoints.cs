using System.Diagnostics;
using System.Linq.Expressions;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

        group.MapGet("/", GetAllAsync)
            .WithName("GetJobApplications")
            .WithSummary("Query job applications")
            .WithDescription(
                "Searches company names and position titles and supports status, source, "
                + "application date, and next-action filters. Results are paged and can only "
                + "be sorted by updatedAt, createdAt, companyName, positionTitle, appliedOn, "
                + "or nextActionDueAt in asc or desc direction.")
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

    private static async Task<Ok<PagedJobApplicationsResponse>> GetAllAsync(
        [AsParameters] GetJobApplicationsQuery queryParameters,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        IQueryable<JobApplication> query = dbContext.JobApplications.AsNoTracking();
        query = ApplyFilters(query, queryParameters);

        int page = queryParameters.Page ?? GetJobApplicationsQuery.DefaultPage;
        int pageSize = queryParameters.PageSize ?? GetJobApplicationsQuery.DefaultPageSize;
        JobApplicationSortField sortBy = queryParameters.GetSortBy();
        JobApplicationSortDirection sortDirection = queryParameters.GetSortDirection();

        int totalCount = await query.CountAsync(cancellationToken);
        int skippedItemCount = (page - 1) * pageSize;

        List<JobApplicationResponse> items = await ApplyOrdering(query, sortBy, sortDirection)
            .Skip(skippedItemCount)
            .Take(pageSize)
            .Select(JobApplicationMappings.ResponseProjection)
            .ToListAsync(cancellationToken);

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return TypedResults.Ok(new PagedJobApplicationsResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages));
    }

    private static IQueryable<JobApplication> ApplyFilters(
        IQueryable<JobApplication> query,
        GetJobApplicationsQuery queryParameters)
    {
        if (!string.IsNullOrWhiteSpace(queryParameters.Search))
        {
            string search = queryParameters.Search.Trim();
            query = query.Where(application =>
                application.CompanyName.Contains(search)
                || application.PositionTitle.Contains(search));
        }

        if (queryParameters.GetStatus() is ApplicationStatus status)
        {
            query = query.Where(application => application.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(queryParameters.Source))
        {
            string source = queryParameters.Source.Trim();
            query = query.Where(application => application.Source == source);
        }

        if (queryParameters.AppliedFrom is DateOnly appliedFrom)
        {
            query = query.Where(application => application.AppliedOn >= appliedFrom);
        }

        if (queryParameters.AppliedTo is DateOnly appliedTo)
        {
            query = query.Where(application => application.AppliedOn <= appliedTo);
        }

        if (queryParameters.NextActionBefore is DateTimeOffset nextActionBefore)
        {
            DateTimeOffset nextActionBeforeUtc = nextActionBefore.ToUniversalTime();
            query = query.Where(application => application.NextActionDueAt <= nextActionBeforeUtc);
        }

        return query;
    }

    private static IOrderedQueryable<JobApplication> ApplyOrdering(
        IQueryable<JobApplication> query,
        JobApplicationSortField sortBy,
        JobApplicationSortDirection sortDirection) =>
        sortBy switch
        {
            JobApplicationSortField.UpdatedAt => OrderBy(
                query,
                application => application.UpdatedAt,
                sortDirection),
            JobApplicationSortField.CreatedAt => OrderBy(
                query,
                application => application.CreatedAt,
                sortDirection),
            JobApplicationSortField.CompanyName => OrderBy(
                query,
                application => application.CompanyName,
                sortDirection),
            JobApplicationSortField.PositionTitle => OrderBy(
                query,
                application => application.PositionTitle,
                sortDirection),
            JobApplicationSortField.AppliedOn => OrderBy(
                query,
                application => application.AppliedOn,
                sortDirection),
            JobApplicationSortField.NextActionDueAt => OrderBy(
                query,
                application => application.NextActionDueAt,
                sortDirection),
            _ => throw new UnreachableException("The sort field was validated before the handler ran."),
        };

    private static IOrderedQueryable<JobApplication> OrderBy<TKey>(
        IQueryable<JobApplication> query,
        Expression<Func<JobApplication, TKey>> keySelector,
        JobApplicationSortDirection sortDirection) =>
        sortDirection switch
        {
            JobApplicationSortDirection.Asc => query
                .OrderBy(keySelector)
                .ThenBy(application => application.Id),
            JobApplicationSortDirection.Desc => query
                .OrderByDescending(keySelector)
                .ThenByDescending(application => application.Id),
            _ => throw new UnreachableException("The sort direction was validated before the handler ran."),
        };

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
