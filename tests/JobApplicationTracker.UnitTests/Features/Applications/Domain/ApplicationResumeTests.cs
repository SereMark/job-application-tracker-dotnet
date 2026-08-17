using JobApplicationTracker.Api.Features.Applications.Domain;
using Xunit;

namespace JobApplicationTracker.UnitTests.Features.Applications.Domain;

public sealed class ApplicationResumeTests
{
    private static readonly DateTimeOffset UploadedAt =
        new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateWithValidFileStoresAnIndependentCopy()
    {
        Guid applicationId = Guid.CreateVersion7();
        byte[] content = "%PDF-1.7 resume"u8.ToArray();

        ApplicationResume resume = ApplicationResume.Create(
            applicationId,
            "  Mark-Resume.pdf  ",
            "  application/pdf  ",
            content,
            UploadedAt);
        content[0] = 0;

        Assert.Equal(applicationId, resume.JobApplicationId);
        Assert.Equal("Mark-Resume.pdf", resume.FileName);
        Assert.Equal("application/pdf", resume.ContentType);
        Assert.Equal((byte)'%', resume.Content[0]);
        Assert.Equal(UploadedAt, resume.UploadedAt);
    }

    [Fact]
    public void ReplaceUpdatesTheStoredFile()
    {
        ApplicationResume resume = CreateResume();
        byte[] replacement = "PK replacement docx"u8.ToArray();

        resume.Replace(
            "Mark-Resume.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            replacement,
            UploadedAt.AddHours(1));

        Assert.Equal("Mark-Resume.docx", resume.FileName);
        Assert.Equal(replacement, resume.Content);
        Assert.Equal(UploadedAt.AddHours(1), resume.UploadedAt);
    }

    [Fact]
    public void CreateWithEmptyContentThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ApplicationResume.Create(
                Guid.CreateVersion7(),
                "Mark-Resume.pdf",
                "application/pdf",
                [],
                UploadedAt));
    }

    private static ApplicationResume CreateResume() =>
        ApplicationResume.Create(
            Guid.CreateVersion7(),
            "Mark-Resume.pdf",
            "application/pdf",
            "%PDF-1.7 resume"u8.ToArray(),
            UploadedAt);
}
