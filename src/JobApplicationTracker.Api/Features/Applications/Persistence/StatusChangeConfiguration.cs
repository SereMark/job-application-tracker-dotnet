using JobApplicationTracker.Api.Features.Applications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobApplicationTracker.Api.Features.Applications.Persistence;

internal sealed class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.ToTable("StatusChanges", table =>
        {
            table.HasCheckConstraint(
                "CK_StatusChanges_PreviousStatus",
                $"[PreviousStatus] IS NULL OR [PreviousStatus] IN ({ApplicationStatusMapping.AllowedValuesSql})");
            table.HasCheckConstraint(
                "CK_StatusChanges_NewStatus",
                $"[NewStatus] IN ({ApplicationStatusMapping.AllowedValuesSql})");
            table.HasCheckConstraint(
                "CK_StatusChanges_DifferentStatuses",
                "[PreviousStatus] IS NULL OR [PreviousStatus] <> [NewStatus]");
        });

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id)
            .ValueGeneratedNever();

        builder.Property(change => change.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(ApplicationStatusMapping.MaxLength)
            .IsUnicode(false);

        builder.Property(change => change.NewStatus)
            .HasConversion<string>()
            .HasMaxLength(ApplicationStatusMapping.MaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(change => change.ChangedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.Property(change => change.Note)
            .HasMaxLength(StatusChange.NoteMaxLength);

        builder.HasOne<JobApplication>()
            .WithMany(application => application.StatusHistory)
            .HasForeignKey(change => change.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(change => new { change.JobApplicationId, change.ChangedAt });
    }
}
