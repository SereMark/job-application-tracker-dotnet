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
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PdfContentType = "application/pdf";
    private const int SummaryWindowDays = 7;
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

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetApplicationSummary")
            .WithSummary("Summarize the application pipeline")
            .WithDescription(
                "Returns total and per-status application counts, plus overdue next actions "
                + "and next actions due within the next seven days.");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName(GetByIdRouteName)
            .WithSummary("Get a job application")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/resume", UploadResumeAsync)
            .WithName("UploadApplicationResume")
            .WithSummary("Upload or replace an application resume")
            .WithDescription(
                "Stores one PDF or DOCX resume of at most 5 MB for the job application. "
                + "Uploading again replaces the previous file.")
            .Produces<ApplicationResumeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();

        group.MapGet("/{id:guid}/resume", DownloadResumeAsync)
            .WithName("DownloadApplicationResume")
            .WithSummary("Download an application resume")
            .WithDescription(
                "Downloads the resume stored for the job application using its original file name.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateJobApplication")
            .WithSummary("Replace job application details")
            .WithDescription(
                "Replaces all editable details and leaves the current status unchanged. "
                + "Omitted optional fields are cleared.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/status", ChangeStatusAsync)
            .WithName("ChangeJobApplicationStatus")
            .WithSummary("Change a job application status")
            .WithDescription(
                "Changes the current status and records the transition in status history. "
                + "Changing to the current status returns a conflict response.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/status-history", GetStatusHistoryAsync)
            .WithName("GetJobApplicationStatusHistory")
            .WithSummary("Get job application status history")
            .WithDescription(
                "Returns the initial status and every later transition in chronological order.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteJobApplication")
            .WithSummary("Delete a job application")
            .WithDescription(
                "Permanently deletes a job application, its status history, and its resume.")
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
            return CreateNotFoundProblem(id);
        }

        return TypedResults.Ok(application.ToResponse());
    }

    private static async Task<
        Results<Ok<ApplicationResumeResponse>, ValidationProblem, ProblemHttpResult>>
        UploadResumeAsync(
            Guid id,
            IFormFile file,
            ApplicationDbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
    {
        bool applicationExists = await dbContext.JobApplications
            .AsNoTracking()
            .AnyAsync(application => application.Id == id, cancellationToken);

        if (!applicationExists)
        {
            return CreateNotFoundProblem(id);
        }

        ResumeFileValidation validation = ValidateResumeFile(file);

        if (validation.Error is not null)
        {
            return CreateResumeValidationProblem(validation.Error);
        }

        using var contentStream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(contentStream, cancellationToken);
        byte[] content = contentStream.ToArray();

        ApplicationResume? resume = await dbContext.ApplicationResumes
            .SingleOrDefaultAsync(
                resume => resume.JobApplicationId == id,
                cancellationToken);

        if (resume is null)
        {
            resume = ApplicationResume.Create(
                id,
                validation.FileName,
                validation.ContentType,
                content,
                timeProvider.GetUtcNow());
            dbContext.ApplicationResumes.Add(resume);
        }
        else
        {
            resume.Replace(
                validation.FileName,
                validation.ContentType,
                content,
                timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(resume.ToResponse());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>>
        DownloadResumeAsync(
            Guid id,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
    {
        ApplicationResume? resume = await dbContext.ApplicationResumes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                resume => resume.JobApplicationId == id,
                cancellationToken);

        if (resume is null)
        {
            return CreateResumeNotFoundProblem(id);
        }

        return TypedResults.File(
            resume.Content,
            resume.ContentType,
            resume.FileName,
            lastModified: resume.UploadedAt);
    }

    private static async Task<Results<Ok<JobApplicationResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateJobApplicationRequest request,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        JobApplication? application = await dbContext.JobApplications
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        if (application is null)
        {
            return CreateNotFoundProblem(id);
        }

        bool wasUpdated = application.UpdateDetails(
            request.ToDetails(),
            timeProvider.GetUtcNow());

        if (wasUpdated)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(application.ToResponse());
    }

    private static async Task<Results<Ok<JobApplicationResponse>, ProblemHttpResult>> ChangeStatusAsync(
        Guid id,
        ChangeJobApplicationStatusRequest request,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        JobApplication? application = await dbContext.JobApplications
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        if (application is null)
        {
            return CreateNotFoundProblem(id);
        }

        ApplicationStatus newStatus = request.Status
            ?? throw new UnreachableException("Status is validated before the handler runs.");

        bool wasChanged = application.ChangeStatus(
            newStatus,
            request.Note,
            timeProvider.GetUtcNow());

        if (!wasChanged)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Job application status conflict",
                detail: $"Job application '{id}' already has status '{newStatus}'.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(application.ToResponse());
    }

    private static async Task<Results<Ok<List<StatusChangeResponse>>, ProblemHttpResult>>
        GetStatusHistoryAsync(
            Guid id,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
    {
        bool applicationExists = await dbContext.JobApplications
            .AsNoTracking()
            .AnyAsync(application => application.Id == id, cancellationToken);

        if (!applicationExists)
        {
            return CreateNotFoundProblem(id);
        }

        List<StatusChangeResponse> history = await dbContext.StatusChanges
            .AsNoTracking()
            .Where(change => change.JobApplicationId == id)
            .OrderBy(change => change.ChangedAt)
            .ThenBy(change => change.Id)
            .Select(JobApplicationMappings.StatusHistoryProjection)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(history);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        JobApplication? application = await dbContext.JobApplications
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        if (application is null)
        {
            return CreateNotFoundProblem(id);
        }

        dbContext.JobApplications.Remove(application);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<ApplicationSummaryResponse>> GetSummaryAsync(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset summaryWindowEnd = now.AddDays(SummaryWindowDays);

        var summaryRows = await dbContext.JobApplications
            .AsNoTracking()
            .GroupBy(application => application.Status)
            .Select(group => new
            {
                Status = group.Key,
                TotalCount = group.Count(),
                OverdueNextActionCount = group.Count(application =>
                    application.NextActionDueAt < now),
                NextActionDueWithinSevenDaysCount = group.Count(application =>
                    application.NextActionDueAt >= now
                    && application.NextActionDueAt <= summaryWindowEnd),
            })
            .ToListAsync(cancellationToken);

        Dictionary<ApplicationStatus, int> countsByStatus = summaryRows.ToDictionary(
            row => row.Status,
            row => row.TotalCount);

        List<ApplicationStatusCountResponse> statusCounts = Enum
            .GetValues<ApplicationStatus>()
            .Select(status => new ApplicationStatusCountResponse(
                status,
                countsByStatus.GetValueOrDefault(status)))
            .ToList();

        return TypedResults.Ok(new ApplicationSummaryResponse(
            TotalCount: summaryRows.Sum(row => row.TotalCount),
            StatusCounts: statusCounts,
            OverdueNextActionCount: summaryRows.Sum(row => row.OverdueNextActionCount),
            NextActionDueWithinSevenDaysCount: summaryRows.Sum(
                row => row.NextActionDueWithinSevenDaysCount)));
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

    private static ProblemHttpResult CreateNotFoundProblem(Guid id) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Job application not found",
            detail: $"No job application with id '{id}' exists.");

    private static ProblemHttpResult CreateResumeNotFoundProblem(Guid id) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Application resume not found",
            detail: $"Job application '{id}' does not have a stored resume.");

    private static ValidationProblem CreateResumeValidationProblem(string error) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["file"] = [error],
            });

    private static ResumeFileValidation ValidateResumeFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            return ResumeFileValidation.Invalid("The resume file cannot be empty.");
        }

        if (file.Length > ApplicationResume.MaxFileSize)
        {
            return ResumeFileValidation.Invalid("The resume file cannot exceed 5 MB.");
        }

        string fileName = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ResumeFileValidation.Invalid("The resume file name is required.");
        }

        if (fileName.Length > ApplicationResume.FileNameMaxLength)
        {
            return ResumeFileValidation.Invalid(
                $"The resume file name cannot exceed {ApplicationResume.FileNameMaxLength} characters.");
        }

        string? contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => PdfContentType,
            ".docx" => DocxContentType,
            _ => null,
        };

        return contentType is null
            ? ResumeFileValidation.Invalid("Only PDF and DOCX resume files are supported.")
            : ResumeFileValidation.Valid(fileName, contentType);
    }

    private sealed record ResumeFileValidation(
        string FileName,
        string ContentType,
        string? Error)
    {
        public static ResumeFileValidation Invalid(string error) =>
            new(string.Empty, string.Empty, error);

        public static ResumeFileValidation Valid(string fileName, string contentType) =>
            new(fileName, contentType, Error: null);
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
