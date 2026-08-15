using System.Linq.Expressions;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications;

internal static class JobApplicationMappings
{
    public static Expression<Func<JobApplication, JobApplicationResponse>> ResponseProjection { get; } =
        application => new JobApplicationResponse(
            application.Id,
            application.CompanyName,
            application.PositionTitle,
            application.JobPostingUrl,
            application.Source,
            application.Location,
            application.Status,
            application.AppliedOn,
            application.Notes,
            application.NextActionDescription,
            application.NextActionDueAt,
            application.CreatedAt,
            application.UpdatedAt);

    public static Expression<Func<StatusChange, StatusChangeResponse>> StatusHistoryProjection { get; } =
        change => new StatusChangeResponse(
            change.Id,
            change.PreviousStatus,
            change.NewStatus,
            change.ChangedAt,
            change.Note);

    public static JobApplicationDetails ToDetails(this CreateJobApplicationRequest request) =>
        new(
            request.CompanyName,
            request.PositionTitle,
            request.JobPostingUrl,
            request.Source,
            request.Location,
            request.AppliedOn,
            request.Notes,
            request.NextActionDescription,
            request.NextActionDueAt);

    public static JobApplicationDetails ToDetails(this UpdateJobApplicationRequest request) =>
        new(
            request.CompanyName,
            request.PositionTitle,
            request.JobPostingUrl,
            request.Source,
            request.Location,
            request.AppliedOn,
            request.Notes,
            request.NextActionDescription,
            request.NextActionDueAt);

    public static JobApplicationResponse ToResponse(this JobApplication application) =>
        new(
            application.Id,
            application.CompanyName,
            application.PositionTitle,
            application.JobPostingUrl,
            application.Source,
            application.Location,
            application.Status,
            application.AppliedOn,
            application.Notes,
            application.NextActionDescription,
            application.NextActionDueAt,
            application.CreatedAt,
            application.UpdatedAt);
}
