using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Features.Applications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobApplicationTracker.Api.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<ApplicationResume> ApplicationResumes => Set<ApplicationResume>();

    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new JobApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new ApplicationResumeConfiguration());
        modelBuilder.ApplyConfiguration(new StatusChangeConfiguration());
    }
}
