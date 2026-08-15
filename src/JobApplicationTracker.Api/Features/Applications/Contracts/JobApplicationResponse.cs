using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record JobApplicationResponse(
    Guid Id,
    string CompanyName,
    string PositionTitle,
    Uri? JobPostingUrl,
    string? Source,
    string? Location,
    ApplicationStatus Status,
    DateOnly? AppliedOn,
    string? Notes,
    string? NextActionDescription,
    DateTimeOffset? NextActionDueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
