using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record ApplicationSummaryResponse(
    int TotalCount,
    IReadOnlyList<ApplicationStatusCountResponse> StatusCounts,
    int OverdueNextActionCount,
    int NextActionDueWithinSevenDaysCount);

public sealed record ApplicationStatusCountResponse(
    ApplicationStatus Status,
    int Count);
