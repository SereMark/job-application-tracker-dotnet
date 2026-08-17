namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record ApplicationResumeResponse(
    string FileName,
    string ContentType,
    int Size,
    DateTimeOffset UploadedAt);
