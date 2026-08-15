using System.ComponentModel.DataAnnotations;
using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

internal static class JobApplicationRequestValidation
{
    public static IEnumerable<ValidationResult> Validate(
        string? companyName,
        string? positionTitle,
        Uri? jobPostingUrl,
        string? nextActionDescription,
        DateTimeOffset? nextActionDueAt)
    {
        if (companyName is not null && string.IsNullOrWhiteSpace(companyName))
        {
            yield return new ValidationResult(
                "Company name cannot contain only whitespace.",
                [nameof(JobApplication.CompanyName)]);
        }

        if (positionTitle is not null && string.IsNullOrWhiteSpace(positionTitle))
        {
            yield return new ValidationResult(
                "Position title cannot contain only whitespace.",
                [nameof(JobApplication.PositionTitle)]);
        }

        if (jobPostingUrl is not null)
        {
            bool isHttpUrl = jobPostingUrl.IsAbsoluteUri
                && (jobPostingUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || jobPostingUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

            if (!isHttpUrl)
            {
                yield return new ValidationResult(
                    "The job posting URL must be an absolute HTTP or HTTPS URL.",
                    [nameof(JobApplication.JobPostingUrl)]);
            }
            else if (jobPostingUrl.AbsoluteUri.Length > JobApplication.JobPostingUrlMaxLength)
            {
                yield return new ValidationResult(
                    $"The job posting URL cannot exceed {JobApplication.JobPostingUrlMaxLength} characters.",
                    [nameof(JobApplication.JobPostingUrl)]);
            }
        }

        bool hasNextActionDescription = !string.IsNullOrWhiteSpace(nextActionDescription);
        bool hasNextActionDueAt = nextActionDueAt.HasValue;

        if (hasNextActionDescription != hasNextActionDueAt)
        {
            yield return new ValidationResult(
                "Next action description and due date must either both be provided or both be omitted.",
                [
                    nameof(JobApplication.NextActionDescription),
                    nameof(JobApplication.NextActionDueAt),
                ]);
        }
    }
}
