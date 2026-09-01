using System.ComponentModel.DataAnnotations;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Contracts;

public sealed class SharePointItemResponse
{
    public string Id { get; set; } = "";
    public string? WebUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Dictionary<string, object?> Fields { get; set; } = [];
}

public sealed record SharePointListResponse(
    string Id,
    string? DisplayName,
    string? Name,
    string? WebUrl,
    string? Template);

public sealed record SharePointSetupResource(
    string Id,
    string? DisplayName,
    string? Name,
    string? WebUrl,
    string? Template,
    string Status);

public sealed record SharePointSetupResponse(
    string SiteId,
    string? SiteUrl,
    IReadOnlyList<SharePointSetupResource> Resources);

public sealed record SharePointSyncResponse(
    string Message,
    int Candidates,
    int Jobs,
    int Applications,
    int ResumesUploaded);

public sealed class SharePointCandidateCreateRequest
{
    [MaxLength(255)] public string? CandidateName { get; init; }
    [Required, EmailAddress, MaxLength(255)] public string Email { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string LastName { get; init; } = "";
    [MaxLength(32)] public string CountryCode { get; init; } = "+966";
    [MaxLength(40)] public string Phone { get; init; } = "";
    [MaxLength(100)] public string Country { get; init; } = "Saudi Arabia";
    [MaxLength(100)] public string City { get; init; } = "";
    [MaxLength(150)] public string ProfessionalTitle { get; init; } = "Candidate";
    [MaxLength(2000)] public string About { get; init; } = "";
    public string Role { get; init; } = "Candidate";
    public string? ResumeUrl { get; init; }

    public Dictionary<string, object?> ToFields() => new()
    {
        ["Title"] = string.IsNullOrWhiteSpace(CandidateName)
            ? $"{FirstName} {LastName}".Trim()
            : CandidateName.Trim(),
        ["Email"] = Email.Trim().ToLowerInvariant(),
        ["FirstName"] = FirstName.Trim(),
        ["LastName"] = LastName.Trim(),
        ["CountryCode"] = CountryCode.Trim(),
        ["Phone"] = Phone.Trim(),
        ["Country"] = Country.Trim(),
        ["City"] = City.Trim(),
        ["ProfessionalTitle"] = ProfessionalTitle.Trim(),
        ["About"] = About.Trim(),
        ["Role"] = Role,
        ["ResumeUrl"] = ResumeUrl,
    };
}

public sealed class SharePointCandidateUpdateRequest
{
    [MaxLength(255)] public string? CandidateName { get; init; }
    [EmailAddress, MaxLength(255)] public string? Email { get; init; }
    [MinLength(1), MaxLength(100)] public string? FirstName { get; init; }
    [MinLength(1), MaxLength(100)] public string? LastName { get; init; }
    [MaxLength(32)] public string? CountryCode { get; init; }
    [MaxLength(40)] public string? Phone { get; init; }
    [MaxLength(100)] public string? Country { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(150)] public string? ProfessionalTitle { get; init; }
    [MaxLength(2000)] public string? About { get; init; }
    public string? Role { get; init; }
    public string? ResumeUrl { get; init; }

    public Dictionary<string, object?> ToFields() => SharePointFieldMappings.OptionalFields(
        ("Title", CandidateName), ("Email", Email?.ToLowerInvariant()), ("FirstName", FirstName),
        ("LastName", LastName), ("CountryCode", CountryCode), ("Phone", Phone),
        ("Country", Country), ("City", City), ("ProfessionalTitle", ProfessionalTitle),
        ("About", About), ("Role", Role), ("ResumeUrl", ResumeUrl));
}

public sealed class SharePointJobCreateRequest
{
    [Required, MinLength(1), MaxLength(180)] public string JobTitle { get; init; } = "";
    [Required, MinLength(1), MaxLength(120)] public string Division { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string Country { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string City { get; init; } = "";
    [Required, MinLength(1), MaxLength(120)] public string JobFunction { get; init; } = "";
    [Required] public string CareerLevel { get; init; } = "";
    [Required] public string EmploymentType { get; init; } = "Full-time";
    [Required] public string Summary { get; init; } = "";
    [Required] public string Description { get; init; } = "";
    [Required] public string Requirements { get; init; } = "";
    public bool IsOpen { get; init; } = true;
    public bool IsFeatured { get; init; }
    public DateTime PostedAt { get; init; } = PortalClock.UtcNow();
    public DateTime? ExpiresAt { get; init; }

    public Dictionary<string, object?> ToFields() => new()
    {
        ["Title"] = JobTitle.Trim(),
        ["Division"] = Division.Trim(),
        ["Country"] = Country.Trim(),
        ["City"] = City.Trim(),
        ["JobFunction"] = JobFunction.Trim(),
        ["CareerLevel"] = CareerLevel,
        ["EmploymentType"] = EmploymentType,
        ["Summary"] = Summary.Trim(),
        ["Description"] = Description.Trim(),
        ["Requirements"] = Requirements.Trim(),
        ["IsOpen"] = IsOpen,
        ["IsFeatured"] = IsFeatured,
        ["PostedAt"] = Services.SharePointSyncService.GraphDateTime(PostedAt),
        ["ExpiresAt"] = ExpiresAt is null ? null : Services.SharePointSyncService.GraphDateTime(ExpiresAt.Value),
    };
}

public sealed class SharePointJobUpdateRequest
{
    [MinLength(1), MaxLength(180)] public string? JobTitle { get; init; }
    [MinLength(1), MaxLength(120)] public string? Division { get; init; }
    [MinLength(1), MaxLength(100)] public string? Country { get; init; }
    [MinLength(1), MaxLength(100)] public string? City { get; init; }
    [MinLength(1), MaxLength(120)] public string? JobFunction { get; init; }
    public string? CareerLevel { get; init; }
    public string? EmploymentType { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? Requirements { get; init; }
    public bool? IsOpen { get; init; }
    public bool? IsFeatured { get; init; }
    public DateTime? PostedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }

    public Dictionary<string, object?> ToFields() => SharePointFieldMappings.OptionalFields(
        ("Title", JobTitle), ("Division", Division), ("Country", Country), ("City", City),
        ("JobFunction", JobFunction), ("CareerLevel", CareerLevel), ("EmploymentType", EmploymentType),
        ("Summary", Summary), ("Description", Description), ("Requirements", Requirements),
        ("IsOpen", IsOpen), ("IsFeatured", IsFeatured),
        ("PostedAt", PostedAt is null ? null : Services.SharePointSyncService.GraphDateTime(PostedAt.Value)),
        ("ExpiresAt", ExpiresAt is null ? null : Services.SharePointSyncService.GraphDateTime(ExpiresAt.Value)));
}

public sealed class SharePointApplicationCreateRequest
{
    [MaxLength(100)] public string? ApplicationCode { get; init; }
    [Range(1, int.MaxValue)] public int CandidateId { get; init; }
    [Range(1, int.MaxValue)] public int JobId { get; init; }
    public string Status { get; init; } = "Under Review";
    public DateTime AppliedAt { get; init; } = PortalClock.UtcNow();

    public Dictionary<string, object?> ToFields() => new()
    {
        ["Title"] = string.IsNullOrWhiteSpace(ApplicationCode)
            ? $"APP-{CandidateId}-{JobId}"
            : ApplicationCode.Trim(),
        ["CandidateLookupId"] = CandidateId.ToString(),
        ["JobLookupId"] = JobId.ToString(),
        ["CandidateJobKey"] = $"{CandidateId}:{JobId}",
        ["Status"] = Status,
        ["AppliedAt"] = Services.SharePointSyncService.GraphDateTime(AppliedAt),
    };
}

public sealed class SharePointApplicationUpdateRequest
{
    [MaxLength(100)] public string? ApplicationCode { get; init; }
    public string? Status { get; init; }
    public DateTime? AppliedAt { get; init; }

    public Dictionary<string, object?> ToFields() => SharePointFieldMappings.OptionalFields(
        ("Title", ApplicationCode), ("Status", Status),
        ("AppliedAt", AppliedAt is null ? null : Services.SharePointSyncService.GraphDateTime(AppliedAt.Value)));
}

internal static class SharePointFieldMappings
{
    public static Dictionary<string, object?> OptionalFields(params (string Name, object? Value)[] values) =>
        values.Where(value => value.Value is not null).ToDictionary(
            value => value.Name,
            value => value.Value is string text ? text.Trim() : value.Value);
}
