namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record PagedJobApplicationsResponse(
    IReadOnlyList<JobApplicationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
