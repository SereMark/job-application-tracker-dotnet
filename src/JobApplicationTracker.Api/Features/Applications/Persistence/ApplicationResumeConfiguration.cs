using JobApplicationTracker.Api.Features.Applications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobApplicationTracker.Api.Features.Applications.Persistence;

internal sealed class ApplicationResumeConfiguration : IEntityTypeConfiguration<ApplicationResume>
{
    public void Configure(EntityTypeBuilder<ApplicationResume> builder)
    {
        builder.ToTable("ApplicationResumes", table =>
        {
            table.HasCheckConstraint(
                "CK_ApplicationResumes_ContentLength",
                $"DATALENGTH([Content]) BETWEEN 1 AND {ApplicationResume.MaxFileSize}");
        });

        builder.HasKey(resume => resume.JobApplicationId);

        builder.Property(resume => resume.JobApplicationId)
            .ValueGeneratedNever();

        builder.Property(resume => resume.FileName)
            .HasMaxLength(ApplicationResume.FileNameMaxLength)
            .IsRequired();

        builder.Property(resume => resume.ContentType)
            .HasMaxLength(ApplicationResume.ContentTypeMaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(resume => resume.Content)
            .HasColumnType("varbinary(max)")
            .IsRequired();

        builder.Property(resume => resume.UploadedAt)
            .HasPrecision(7)
            .IsRequired();

        builder.HasOne<JobApplication>()
            .WithOne()
            .HasForeignKey<ApplicationResume>(resume => resume.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
