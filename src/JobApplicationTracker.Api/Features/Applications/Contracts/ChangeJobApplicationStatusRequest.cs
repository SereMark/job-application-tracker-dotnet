using System.ComponentModel.DataAnnotations;
using JobApplicationTracker.Api.Features.Applications.Domain;

namespace JobApplicationTracker.Api.Features.Applications.Contracts;

public sealed record ChangeJobApplicationStatusRequest(
    [property: Required]
    [property: EnumDataType(typeof(ApplicationStatus))]
    ApplicationStatus? Status,
    [property: MaxLength(StatusChange.NoteMaxLength)]
    string? Note = null);
