using System.ComponentModel.DataAnnotations;
using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record UpdateJobApplicationRequest(
    [property: Required]
    [property: MaxLength(JobApplication.CompanyNameMaxLength)]
    string CompanyName,
    [property: Required]
    [property: MaxLength(JobApplication.PositionTitleMaxLength)]
    string PositionTitle,
    Uri? JobPostingUrl = null,
    [property: MaxLength(JobApplication.SourceMaxLength)]
    string? Source = null,
    [property: MaxLength(JobApplication.LocationMaxLength)]
    string? Location = null,
    DateOnly? AppliedOn = null,
    [property: MaxLength(JobApplication.NotesMaxLength)]
    string? Notes = null,
    [property: MaxLength(JobApplication.NextActionDescriptionMaxLength)]
    string? NextActionDescription = null,
    DateTimeOffset? NextActionDueAt = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        JobApplicationRequestValidation.Validate(
            CompanyName,
            PositionTitle,
            JobPostingUrl,
            NextActionDescription,
            NextActionDueAt);
}
