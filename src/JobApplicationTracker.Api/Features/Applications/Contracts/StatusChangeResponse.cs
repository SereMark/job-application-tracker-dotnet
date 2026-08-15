using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record StatusChangeResponse(
    Guid Id,
    ApplicationStatus? PreviousStatus,
    ApplicationStatus NewStatus,
    DateTimeOffset ChangedAt,
    string? Note);
