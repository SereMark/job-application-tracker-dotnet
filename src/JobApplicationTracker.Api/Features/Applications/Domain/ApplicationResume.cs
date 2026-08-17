namespace JobApplicationTracker.Api.Features.Applications.Domain;

public sealed class ApplicationResume
{
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const int MaxFileSize = 5 * 1_024 * 1_024;

    private ApplicationResume()
    {
    }

    public Guid JobApplicationId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public DateTimeOffset UploadedAt { get; private set; }

    public static ApplicationResume Create(
        Guid jobApplicationId,
        string fileName,
        string contentType,
        byte[] content,
        DateTimeOffset uploadedAt)
    {
        if (jobApplicationId == Guid.Empty)
        {
            throw new ArgumentException("A job application id is required.", nameof(jobApplicationId));
        }

        var resume = new ApplicationResume
        {
            JobApplicationId = jobApplicationId,
        };

        resume.Replace(fileName, contentType, content, uploadedAt);
        return resume;
    }

    public void Replace(
        string fileName,
        string contentType,
        byte[] content,
        DateTimeOffset uploadedAt)
    {
        FileName = ValidateRequiredText(
            fileName,
            FileNameMaxLength,
            nameof(fileName));
        ContentType = ValidateRequiredText(
            contentType,
            ContentTypeMaxLength,
            nameof(contentType));

        ArgumentNullException.ThrowIfNull(content);

        if (content.Length is 0 or > MaxFileSize)
        {
            throw new ArgumentException(
                $"Resume content must contain between 1 and {MaxFileSize} bytes.",
                nameof(content));
        }

        DateTimeOffset uploadedAtUtc = uploadedAt.ToUniversalTime();

        if (uploadedAtUtc < DateTimeOffset.UnixEpoch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uploadedAt),
                uploadedAt,
                "The upload time cannot be before the Unix epoch.");
        }

        Content = content.ToArray();
        UploadedAt = uploadedAtUtc;
    }

    private static string ValidateRequiredText(
        string value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        string normalizedValue = value.Trim();

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maxLength} characters.",
                parameterName);
        }

        return normalizedValue;
    }
}
