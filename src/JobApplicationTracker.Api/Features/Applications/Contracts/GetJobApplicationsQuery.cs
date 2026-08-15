using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using JobApplicationTracker.Api.Features.Applications.Domain;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed class GetJobApplicationsQuery : IValidatableObject
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    [FromQuery(Name = "search")]
    [MaxLength(JobApplication.CompanyNameMaxLength)]
    public string? Search { get; init; }

    [FromQuery(Name = "status")]
    [MaxLength(20)]
    public string? Status { get; init; }

    [FromQuery(Name = "source")]
    [MaxLength(JobApplication.SourceMaxLength)]
    public string? Source { get; init; }

    [FromQuery(Name = "appliedFrom")]
    public DateOnly? AppliedFrom { get; init; }

    [FromQuery(Name = "appliedTo")]
    public DateOnly? AppliedTo { get; init; }

    [FromQuery(Name = "nextActionBefore")]
    public DateTimeOffset? NextActionBefore { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    [FromQuery(Name = "sortBy")]
    [MaxLength(50)]
    public string? SortBy { get; init; }

    [FromQuery(Name = "sortDirection")]
    [MaxLength(10)]
    public string? SortDirection { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsDefinedEnumValue<ApplicationStatus>(Status))
        {
            yield return new ValidationResult(
                $"Status must be one of: {string.Join(", ", Enum.GetNames<ApplicationStatus>())}.",
                [nameof(Status)]);
        }

        if (!IsDefinedEnumValue<JobApplicationSortField>(SortBy))
        {
            yield return new ValidationResult(
                $"Sort by must be one of: {string.Join(", ", Enum.GetNames<JobApplicationSortField>())}.",
                [nameof(SortBy)]);
        }

        if (!IsDefinedEnumValue<JobApplicationSortDirection>(SortDirection))
        {
            yield return new ValidationResult(
                $"Sort direction must be one of: {string.Join(", ", Enum.GetNames<JobApplicationSortDirection>())}.",
                [nameof(SortDirection)]);
        }

        if (AppliedFrom > AppliedTo)
        {
            yield return new ValidationResult(
                "Applied from cannot be later than applied to.",
                [nameof(AppliedFrom), nameof(AppliedTo)]);
        }

        int page = Page ?? DefaultPage;
        int pageSize = PageSize ?? DefaultPageSize;
        long skippedItemCount = ((long)page - 1) * pageSize;

        if (page >= 1 && pageSize >= 1 && skippedItemCount > int.MaxValue)
        {
            yield return new ValidationResult(
                "The requested page is too far beyond the available range.",
                [nameof(Page), nameof(PageSize)]);
        }
    }

    internal ApplicationStatus? GetStatus() =>
        string.IsNullOrWhiteSpace(Status)
            ? null
            : ParseDefinedEnum<ApplicationStatus>(Status);

    internal JobApplicationSortField GetSortBy() =>
        string.IsNullOrWhiteSpace(SortBy)
            ? JobApplicationSortField.UpdatedAt
            : ParseDefinedEnum<JobApplicationSortField>(SortBy);

    internal JobApplicationSortDirection GetSortDirection() =>
        string.IsNullOrWhiteSpace(SortDirection)
            ? JobApplicationSortDirection.Desc
            : ParseDefinedEnum<JobApplicationSortDirection>(SortDirection);

    private static bool IsDefinedEnumValue<TEnum>(string? value)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value)
        || (Enum.TryParse(value, ignoreCase: true, out TEnum parsedValue)
            && Enum.IsDefined(parsedValue));

    private static TEnum ParseDefinedEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out TEnum parsedValue)
            && Enum.IsDefined(parsedValue)
                ? parsedValue
                : throw new UnreachableException("Query values are validated before the handler runs.");
}

internal enum JobApplicationSortField
{
    UpdatedAt = 0,
    CreatedAt = 1,
    CompanyName = 2,
    PositionTitle = 3,
    AppliedOn = 4,
    NextActionDueAt = 5,
}

internal enum JobApplicationSortDirection
{
    Asc = 0,
    Desc = 1,
}
