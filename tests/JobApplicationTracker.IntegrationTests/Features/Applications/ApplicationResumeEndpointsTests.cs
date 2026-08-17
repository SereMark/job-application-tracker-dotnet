using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobApplicationTracker.Api.Features.Applications.Contracts;
using JobApplicationTracker.Api.Features.Applications.Domain;
using JobApplicationTracker.Api.Infrastructure.Persistence;
using JobApplicationTracker.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobApplicationTracker.IntegrationTests.Features.Applications;

public sealed class ApplicationResumeEndpointsTests(SqlServerContainerFixture sqlServer)
{
    private static readonly DateTimeOffset UploadedAt =
        new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UploadThenDownloadPersistsAndReturnsResume()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(UploadedAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        byte[] content = "%PDF-1.7 portfolio resume"u8.ToArray();
        using MultipartFormDataContent form = CreateResumeForm(
            content,
            @"C:\fakepath\Mark-Resume.pdf");

        using HttpResponseMessage uploadResponse = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            form,
            cancellationToken);
        ApplicationResumeResponse? uploaded = await uploadResponse.Content
            .ReadFromJsonAsync<ApplicationResumeResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploaded);
        Assert.Equal("Mark-Resume.pdf", uploaded.FileName);
        Assert.Equal("application/pdf", uploaded.ContentType);
        Assert.Equal(content.Length, uploaded.Size);
        Assert.Equal(UploadedAt, uploaded.UploadedAt);

        using HttpResponseMessage downloadResponse = await client.GetAsync(
            $"/api/applications/{applicationId}/resume",
            cancellationToken);
        byte[] downloadedContent = await downloadResponse.Content
            .ReadAsByteArrayAsync(cancellationToken);
        ContentDispositionHeaderValue? contentDisposition =
            downloadResponse.Content.Headers.ContentDisposition;
        string? downloadedFileName = contentDisposition?.FileNameStar
            ?? contentDisposition?.FileName?.Trim('"');

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", contentDisposition?.DispositionType);
        Assert.Equal("Mark-Resume.pdf", downloadedFileName);
        Assert.Equal(UploadedAt, downloadResponse.Content.Headers.LastModified);
        Assert.Equal(content, downloadedContent);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationResume persistedResume = await dbContext.ApplicationResumes
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(applicationId, persistedResume.JobApplicationId);
        Assert.Equal("Mark-Resume.pdf", persistedResume.FileName);
        Assert.Equal(content, persistedResume.Content);
        Assert.Equal(UploadedAt, persistedResume.UploadedAt);
    }

    [Fact]
    public async Task UploadAgainReplacesPreviousResume()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new ManualTimeProvider(UploadedAt);
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                timeProvider,
                cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        using MultipartFormDataContent firstForm = CreateResumeForm(
            "%PDF-1.7 first resume"u8.ToArray(),
            "First-Resume.pdf");
        using HttpResponseMessage firstResponse = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            firstForm,
            cancellationToken);
        timeProvider.Advance(TimeSpan.FromHours(1));
        byte[] replacementContent = "PK replacement resume"u8.ToArray();
        using MultipartFormDataContent replacementForm = CreateResumeForm(
            replacementContent,
            "Tailored-Resume.docx");

        using HttpResponseMessage replacementResponse = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            replacementForm,
            cancellationToken);
        ApplicationResumeResponse? replacement = await replacementResponse.Content
            .ReadFromJsonAsync<ApplicationResumeResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        Assert.NotNull(replacement);
        Assert.Equal("Tailored-Resume.docx", replacement.FileName);
        Assert.Equal(UploadedAt.AddHours(1), replacement.UploadedAt);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationResume persistedResume = await dbContext.ApplicationResumes
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal("Tailored-Resume.docx", persistedResume.FileName);
        Assert.Equal(replacementContent, persistedResume.Content);
    }

    [Fact]
    public async Task UploadUnsupportedFileReturnsValidationProblem()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        using MultipartFormDataContent form = CreateResumeForm(
            "plain text"u8.ToArray(),
            "Resume.txt");

        using HttpResponseMessage response = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            form,
            cancellationToken);
        HttpValidationProblemDetails? problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Contains("file", problem.Errors);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await dbContext.ApplicationResumes.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task UploadOversizedFileReturnsValidationProblem()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        byte[] oversizedContent = new byte[ApplicationResume.MaxFileSize + 1];
        using MultipartFormDataContent form = CreateResumeForm(
            oversizedContent,
            "Resume.pdf");

        using HttpResponseMessage response = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            form,
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResumeEndpointsReturnNotFoundWhenResourceIsMissing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        Guid unknownId = Guid.CreateVersion7();
        using MultipartFormDataContent form = CreateResumeForm(
            "%PDF-1.7 resume"u8.ToArray(),
            "Resume.pdf");

        using HttpResponseMessage uploadResponse = await client.PutAsync(
            $"/api/applications/{unknownId}/resume",
            form,
            cancellationToken);
        using HttpResponseMessage downloadResponse = await client.GetAsync(
            $"/api/applications/{applicationId}/resume",
            cancellationToken);
        ProblemDetails? downloadProblem = await downloadResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, uploadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
        Assert.NotNull(downloadProblem);
        Assert.Equal("Application resume not found", downloadProblem.Title);
    }

    [Fact]
    public async Task DeleteApplicationCascadesStoredResume()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using JobApplicationTrackerApiFactory factory =
            await JobApplicationTrackerApiFactory.CreateAsync(
                sqlServer.ConnectionString,
                cancellationToken: cancellationToken);
        Guid applicationId = await SeedApplicationAsync(factory, cancellationToken);
        using HttpClient client = factory.CreateClient();
        using MultipartFormDataContent form = CreateResumeForm(
            "%PDF-1.7 resume"u8.ToArray(),
            "Resume.pdf");
        using HttpResponseMessage uploadResponse = await client.PutAsync(
            $"/api/applications/{applicationId}/resume",
            form,
            cancellationToken);

        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/applications/{applicationId}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(await dbContext.ApplicationResumes.AnyAsync(cancellationToken));
    }

    private static MultipartFormDataContent CreateResumeForm(
        byte[] content,
        string fileName)
    {
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", fileName);
        return form;
    }

    private static async Task<Guid> SeedApplicationAsync(
        JobApplicationTrackerApiFactory factory,
        CancellationToken cancellationToken)
    {
        JobApplication application = JobApplication.Create(
            new JobApplicationDetails("Example Ltd.", ".NET Developer"),
            ApplicationStatus.Applied,
            UploadedAt.AddDays(-1));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync(cancellationToken);
        return application.Id;
    }
}
