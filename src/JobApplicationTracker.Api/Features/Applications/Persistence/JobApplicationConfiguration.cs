using JobApplicationTracker.Api.Features.Applications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobApplicationTracker.Api.Features.Applications.Persistence;

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        var jobPostingUrlConverter = new ValueConverter<Uri?, string?>(
            uri => uri == null ? null : uri.AbsoluteUri,
            value => value == null ? null : new Uri(value, UriKind.Absolute));

        builder.ToTable("JobApplications", table =>
        {
            table.HasCheckConstraint(
                "CK_JobApplications_Status",
                $"[Status] IN ({ApplicationStatusMapping.AllowedValuesSql})");
            table.HasCheckConstraint(
                "CK_JobApplications_NextActionPair",
                "([NextActionDescription] IS NULL AND [NextActionDueAt] IS NULL) "
                + "OR ([NextActionDescription] IS NOT NULL AND [NextActionDueAt] IS NOT NULL)");
        });

        builder.HasKey(application => application.Id);

        builder.Property(application => application.Id)
            .ValueGeneratedNever();

        builder.Property(application => application.CompanyName)
            .HasMaxLength(JobApplication.CompanyNameMaxLength)
            .IsRequired();

        builder.Property(application => application.PositionTitle)
            .HasMaxLength(JobApplication.PositionTitleMaxLength)
            .IsRequired();

        builder.Property(application => application.JobPostingUrl)
            .HasConversion(jobPostingUrlConverter)
            .HasMaxLength(JobApplication.JobPostingUrlMaxLength);

        builder.Property(application => application.Source)
            .HasMaxLength(JobApplication.SourceMaxLength);

        builder.Property(application => application.Location)
            .HasMaxLength(JobApplication.LocationMaxLength);

        builder.Property(application => application.AppliedOn)
            .HasColumnType("date");

        builder.Property(application => application.Notes)
            .HasMaxLength(JobApplication.NotesMaxLength);

        builder.Property(application => application.NextActionDescription)
            .HasMaxLength(JobApplication.NextActionDescriptionMaxLength);

        builder.Property(application => application.NextActionDueAt)
            .HasPrecision(7);

        builder.Property(application => application.Status)
            .HasConversion<string>()
            .HasMaxLength(ApplicationStatusMapping.MaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(application => application.CreatedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.Property(application => application.UpdatedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.HasIndex(application => new { application.Status, application.UpdatedAt })
            .IsDescending(false, true);

        builder.HasIndex(application => application.NextActionDueAt);

        builder.Navigation(application => application.StatusHistory)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal static class ApplicationStatusMapping
{
    public const int MaxLength = 20;

    public static string AllowedValuesSql { get; } = string.Join(
        ", ",
        Enum.GetNames<ApplicationStatus>().Select(status => $"'{status}'"));
}
