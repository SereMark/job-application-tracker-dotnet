using System.ComponentModel.DataAnnotations;
using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record CreateJobApplicationRequest(
    [property: Required]
    [property: MaxLength(JobApplication.CompanyNameMaxLength)]
    string CompanyName,
    [property: Required]
    [property: MaxLength(JobApplication.PositionTitleMaxLength)]
    string PositionTitle,
    [property: EnumDataType(typeof(ApplicationStatus))]
    ApplicationStatus Status = ApplicationStatus.Saved,
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
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (JobPostingUrl is not null)
        {
            bool isHttpUrl = JobPostingUrl.IsAbsoluteUri
                && (JobPostingUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || JobPostingUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

            if (!isHttpUrl)
            {
                yield return new ValidationResult(
                    "The job posting URL must be an absolute HTTP or HTTPS URL.",
                    [nameof(JobPostingUrl)]);
            }
            else if (JobPostingUrl.AbsoluteUri.Length > JobApplication.JobPostingUrlMaxLength)
            {
                yield return new ValidationResult(
                    $"The job posting URL cannot exceed {JobApplication.JobPostingUrlMaxLength} characters.",
                    [nameof(JobPostingUrl)]);
            }
        }

        bool hasNextActionDescription = !string.IsNullOrWhiteSpace(NextActionDescription);
        bool hasNextActionDueAt = NextActionDueAt.HasValue;

        if (hasNextActionDescription != hasNextActionDueAt)
        {
            yield return new ValidationResult(
                "Next action description and due date must either both be provided or both be omitted.",
                [nameof(NextActionDescription), nameof(NextActionDueAt)]);
        }
    }
}
