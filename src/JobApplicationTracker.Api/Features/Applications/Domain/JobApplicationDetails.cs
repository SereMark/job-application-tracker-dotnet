namespace JobApplicationTracker.Api.Features.Applications.Domain;

public sealed record JobApplicationDetails(
    string CompanyName,
    string PositionTitle,
    Uri? JobPostingUrl = null,
    string? Source = null,
    string? Location = null,
    DateOnly? AppliedOn = null,
    string? Notes = null,
    string? NextActionDescription = null,
    DateTimeOffset? NextActionDueAt = null);
