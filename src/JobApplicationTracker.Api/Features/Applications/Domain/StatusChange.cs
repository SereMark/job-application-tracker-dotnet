namespace JobApplicationTracker.Api.Features.Applications.Domain;

public sealed class StatusChange
{
    public const int NoteMaxLength = 500;

    private StatusChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobApplicationId { get; private set; }

    public ApplicationStatus? PreviousStatus { get; private set; }

    public ApplicationStatus NewStatus { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }

    internal static StatusChange Create(
        Guid jobApplicationId,
        ApplicationStatus? previousStatus,
        ApplicationStatus newStatus,
        string? note,
        DateTimeOffset changedAt)
    {
        if (previousStatus == newStatus)
        {
            throw new ArgumentException("The previous and new statuses must be different.", nameof(newStatus));
        }

        return new StatusChange
        {
            Id = Guid.CreateVersion7(changedAt),
            JobApplicationId = jobApplicationId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedAt = changedAt,
            Note = note,
        };
    }
}
