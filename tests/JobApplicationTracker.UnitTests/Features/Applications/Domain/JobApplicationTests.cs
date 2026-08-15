using JobApplicationTracker.Api.Features.Applications.Domain;
using Xunit;

namespace JobApplicationTracker.UnitTests.Features.Applications.Domain;

public sealed class JobApplicationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateWithValidDetailsCreatesApplicationAndInitialHistory()
    {
        var details = new JobApplicationDetails(
            CompanyName: "  Example Ltd.  ",
            PositionTitle: "  .NET Developer  ",
            JobPostingUrl: new Uri("https://example.com/jobs/42"),
            Source: "  LinkedIn  ",
            Location: "  Budapest  ",
            AppliedOn: new DateOnly(2026, 8, 15),
            Notes: "  Referral from a former colleague.  ",
            NextActionDescription: "  Follow up with the recruiter  ",
            NextActionDueAt: CreatedAt.AddDays(3));

        JobApplication application = JobApplication.Create(
            details,
            ApplicationStatus.Applied,
            CreatedAt);

        Assert.Equal('7', application.Id.ToString("D")[14]);
        Assert.Equal("Example Ltd.", application.CompanyName);
        Assert.Equal(".NET Developer", application.PositionTitle);
        Assert.Equal("LinkedIn", application.Source);
        Assert.Equal("Budapest", application.Location);
        Assert.Equal("Referral from a former colleague.", application.Notes);
        Assert.Equal("Follow up with the recruiter", application.NextActionDescription);
        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(CreatedAt, application.CreatedAt);
        Assert.Equal(CreatedAt, application.UpdatedAt);

        StatusChange initialChange = Assert.Single(application.StatusHistory);
        Assert.Null(initialChange.PreviousStatus);
        Assert.Equal(ApplicationStatus.Applied, initialChange.NewStatus);
        Assert.Equal(CreatedAt, initialChange.ChangedAt);
        Assert.Null(initialChange.Note);
    }

    [Fact]
    public void CreateWithRelativeJobPostingUrlThrowsArgumentException()
    {
        JobApplicationDetails details = CreateValidDetails() with
        {
            JobPostingUrl = new Uri("/jobs/42", UriKind.Relative),
        };

        Assert.Throws<ArgumentException>(() =>
            JobApplication.Create(details, ApplicationStatus.Saved, CreatedAt));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Contact recruiter", false)]
    public void CreateWithIncompleteNextActionThrowsArgumentException(
        string? description,
        bool hasDueDate)
    {
        JobApplicationDetails details = CreateValidDetails() with
        {
            NextActionDescription = description,
            NextActionDueAt = hasDueDate ? CreatedAt.AddDays(1) : null,
        };

        Assert.Throws<ArgumentException>(() =>
            JobApplication.Create(details, ApplicationStatus.Saved, CreatedAt));
    }

    [Fact]
    public void CreateWithBlankCompanyNameThrowsArgumentException()
    {
        JobApplicationDetails details = CreateValidDetails() with
        {
            CompanyName = "   ",
        };

        Assert.Throws<ArgumentException>(() =>
            JobApplication.Create(details, ApplicationStatus.Saved, CreatedAt));
    }

    [Fact]
    public void UpdateDetailsWithChangedDetailsReplacesEditableFields()
    {
        JobApplication application = CreateApplication();
        DateTimeOffset updatedAt = CreatedAt.AddHours(1);
        var changedDetails = new JobApplicationDetails(
            CompanyName: "New Company",
            PositionTitle: "Senior .NET Developer",
            Notes: "Updated notes");

        bool wasUpdated = application.UpdateDetails(changedDetails, updatedAt);

        Assert.True(wasUpdated);
        Assert.Equal("New Company", application.CompanyName);
        Assert.Equal("Senior .NET Developer", application.PositionTitle);
        Assert.Equal("Updated notes", application.Notes);
        Assert.Null(application.JobPostingUrl);
        Assert.Null(application.NextActionDescription);
        Assert.Equal(updatedAt, application.UpdatedAt);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
        Assert.Single(application.StatusHistory);
    }

    [Fact]
    public void UpdateDetailsWithUnchangedDetailsDoesNotChangeTimestamp()
    {
        JobApplication application = CreateApplication();

        bool wasUpdated = application.UpdateDetails(
            CreateValidDetails(),
            CreatedAt.AddHours(1));

        Assert.False(wasUpdated);
        Assert.Equal(CreatedAt, application.UpdatedAt);
    }

    [Fact]
    public void ChangeStatusWithDifferentStatusUpdatesApplicationAndAddsHistory()
    {
        JobApplication application = CreateApplication();
        DateTimeOffset changedAt = CreatedAt.AddDays(1);

        bool wasChanged = application.ChangeStatus(
            ApplicationStatus.Screening,
            "  Recruiter call completed.  ",
            changedAt);

        Assert.True(wasChanged);
        Assert.Equal(ApplicationStatus.Screening, application.Status);
        Assert.Equal(changedAt, application.UpdatedAt);
        Assert.Equal(2, application.StatusHistory.Count);

        StatusChange latestChange = application.StatusHistory.Last();
        Assert.Equal(ApplicationStatus.Saved, latestChange.PreviousStatus);
        Assert.Equal(ApplicationStatus.Screening, latestChange.NewStatus);
        Assert.Equal(changedAt, latestChange.ChangedAt);
        Assert.Equal("Recruiter call completed.", latestChange.Note);
    }

    [Fact]
    public void ChangeStatusWithCurrentStatusDoesNotAddHistory()
    {
        JobApplication application = CreateApplication();

        bool wasChanged = application.ChangeStatus(
            ApplicationStatus.Saved,
            note: null,
            CreatedAt.AddDays(1));

        Assert.False(wasChanged);
        Assert.Equal(CreatedAt, application.UpdatedAt);
        Assert.Single(application.StatusHistory);
    }

    [Fact]
    public void ChangeStatusWithEarlierTimestampThrowsArgumentOutOfRangeException()
    {
        JobApplication application = CreateApplication();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.ChangeStatus(
                ApplicationStatus.Interview,
                note: null,
                CreatedAt.AddMinutes(-1)));
    }

    private static JobApplication CreateApplication() =>
        JobApplication.Create(CreateValidDetails(), ApplicationStatus.Saved, CreatedAt);

    private static JobApplicationDetails CreateValidDetails() =>
        new(
            CompanyName: "Example Ltd.",
            PositionTitle: ".NET Developer",
            JobPostingUrl: new Uri("https://example.com/jobs/42"),
            Source: "LinkedIn",
            Location: "Budapest",
            Notes: "Referral from a former colleague.");
}
