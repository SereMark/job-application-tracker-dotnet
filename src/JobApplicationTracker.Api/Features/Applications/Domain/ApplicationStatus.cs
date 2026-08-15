namespace JobApplicationTracker.Api.Features.Applications.Domain;

public enum ApplicationStatus
{
    Saved = 0,
    Applied = 1,
    Screening = 2,
    Interview = 3,
    Offer = 4,
    Rejected = 5,
    Withdrawn = 6,
}
