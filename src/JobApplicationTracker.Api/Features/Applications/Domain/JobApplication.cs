namespace JobApplicationTracker.Api.Features.Applications.Domain;

public sealed class JobApplication
{
    public const int CompanyNameMaxLength = 200;
    public const int PositionTitleMaxLength = 200;
    public const int JobPostingUrlMaxLength = 2_048;
    public const int SourceMaxLength = 100;
    public const int LocationMaxLength = 200;
    public const int NotesMaxLength = 4_000;
    public const int NextActionDescriptionMaxLength = 500;

    private readonly List<StatusChange> _statusHistory = [];

    private JobApplication()
    {
    }

    public Guid Id { get; private set; }

    public string CompanyName { get; private set; } = string.Empty;

    public string PositionTitle { get; private set; } = string.Empty;

    public Uri? JobPostingUrl { get; private set; }

    public string? Source { get; private set; }

    public string? Location { get; private set; }

    public DateOnly? AppliedOn { get; private set; }

    public string? Notes { get; private set; }

    public string? NextActionDescription { get; private set; }

    public DateTimeOffset? NextActionDueAt { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<StatusChange> StatusHistory => _statusHistory.AsReadOnly();

    public static JobApplication Create(
        JobApplicationDetails details,
        ApplicationStatus initialStatus,
        DateTimeOffset createdAt)
    {
        JobApplicationDetails validatedDetails = ValidateDetails(details);
        EnsureDefinedStatus(initialStatus, nameof(initialStatus));
        DateTimeOffset createdAtUtc = NormalizeEventTime(createdAt, nameof(createdAt));

        var application = new JobApplication
        {
            Id = Guid.CreateVersion7(createdAtUtc),
            Status = initialStatus,
            CreatedAt = createdAtUtc,
            UpdatedAt = createdAtUtc,
        };

        application.ApplyDetails(validatedDetails);
        application._statusHistory.Add(StatusChange.Create(
            application.Id,
            previousStatus: null,
            initialStatus,
            note: null,
            createdAtUtc));

        return application;
    }

    public bool UpdateDetails(JobApplicationDetails details, DateTimeOffset updatedAt)
    {
        JobApplicationDetails validatedDetails = ValidateDetails(details);

        if (HasSameDetails(validatedDetails))
        {
            return false;
        }

        DateTimeOffset updatedAtUtc = NormalizeEventTime(updatedAt, nameof(updatedAt));
        EnsureNotBeforeCurrentState(updatedAtUtc, nameof(updatedAt));

        ApplyDetails(validatedDetails);
        UpdatedAt = updatedAtUtc;

        return true;
    }

    public bool ChangeStatus(
        ApplicationStatus newStatus,
        string? note,
        DateTimeOffset changedAt)
    {
        EnsureDefinedStatus(newStatus, nameof(newStatus));
        string? normalizedNote = NormalizeOptional(note, StatusChange.NoteMaxLength, nameof(note));

        if (newStatus == Status)
        {
            return false;
        }

        DateTimeOffset changedAtUtc = NormalizeEventTime(changedAt, nameof(changedAt));
        EnsureNotBeforeCurrentState(changedAtUtc, nameof(changedAt));

        ApplicationStatus previousStatus = Status;
        Status = newStatus;
        UpdatedAt = changedAtUtc;
        _statusHistory.Add(StatusChange.Create(
            Id,
            previousStatus,
            newStatus,
            normalizedNote,
            changedAtUtc));

        return true;
    }

    private static JobApplicationDetails ValidateDetails(JobApplicationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        string companyName = NormalizeRequired(
            details.CompanyName,
            CompanyNameMaxLength,
            nameof(details.CompanyName));
        string positionTitle = NormalizeRequired(
            details.PositionTitle,
            PositionTitleMaxLength,
            nameof(details.PositionTitle));

        ValidateJobPostingUrl(details.JobPostingUrl);

        string? source = NormalizeOptional(details.Source, SourceMaxLength, nameof(details.Source));
        string? location = NormalizeOptional(details.Location, LocationMaxLength, nameof(details.Location));
        string? notes = NormalizeOptional(details.Notes, NotesMaxLength, nameof(details.Notes));
        string? nextActionDescription = NormalizeOptional(
            details.NextActionDescription,
            NextActionDescriptionMaxLength,
            nameof(details.NextActionDescription));
        DateTimeOffset? nextActionDueAt = details.NextActionDueAt?.ToUniversalTime();

        if ((nextActionDescription is null) != (nextActionDueAt is null))
        {
            throw new ArgumentException(
                "Next action description and due date must either both be provided or both be omitted.",
                nameof(details));
        }

        return details with
        {
            CompanyName = companyName,
            PositionTitle = positionTitle,
            Source = source,
            Location = location,
            Notes = notes,
            NextActionDescription = nextActionDescription,
            NextActionDueAt = nextActionDueAt,
        };
    }

    private static string NormalizeRequired(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        string normalizedValue = value.Trim();
        EnsureMaximumLength(normalizedValue, maxLength, parameterName);

        return normalizedValue;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalizedValue = value.Trim();
        EnsureMaximumLength(normalizedValue, maxLength, parameterName);

        return normalizedValue;
    }

    private static void EnsureMaximumLength(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }
    }

    private static void ValidateJobPostingUrl(Uri? jobPostingUrl)
    {
        if (jobPostingUrl is null)
        {
            return;
        }

        bool isHttpUrl = jobPostingUrl.IsAbsoluteUri
            && (jobPostingUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || jobPostingUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

        if (!isHttpUrl)
        {
            throw new ArgumentException(
                "The job posting URL must be an absolute HTTP or HTTPS URL.",
                nameof(jobPostingUrl));
        }

        EnsureMaximumLength(
            jobPostingUrl.AbsoluteUri,
            JobPostingUrlMaxLength,
            nameof(jobPostingUrl));
    }

    private static void EnsureDefinedStatus(ApplicationStatus status, string parameterName)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(parameterName, status, "The application status is invalid.");
        }
    }

    private static DateTimeOffset NormalizeEventTime(DateTimeOffset eventTime, string parameterName)
    {
        DateTimeOffset eventTimeUtc = eventTime.ToUniversalTime();

        if (eventTimeUtc < DateTimeOffset.UnixEpoch)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                eventTime,
                "The event time cannot be before the Unix epoch.");
        }

        return eventTimeUtc;
    }

    private void EnsureNotBeforeCurrentState(DateTimeOffset eventTime, string parameterName)
    {
        if (eventTime < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                eventTime,
                "The event time cannot be earlier than the current state timestamp.");
        }
    }

    private bool HasSameDetails(JobApplicationDetails details) =>
        CompanyName == details.CompanyName
        && PositionTitle == details.PositionTitle
        && Equals(JobPostingUrl, details.JobPostingUrl)
        && Source == details.Source
        && Location == details.Location
        && AppliedOn == details.AppliedOn
        && Notes == details.Notes
        && NextActionDescription == details.NextActionDescription
        && NextActionDueAt == details.NextActionDueAt;

    private void ApplyDetails(JobApplicationDetails details)
    {
        CompanyName = details.CompanyName;
        PositionTitle = details.PositionTitle;
        JobPostingUrl = details.JobPostingUrl;
        Source = details.Source;
        Location = details.Location;
        AppliedOn = details.AppliedOn;
        Notes = details.Notes;
        NextActionDescription = details.NextActionDescription;
        NextActionDueAt = details.NextActionDueAt;
    }
}
