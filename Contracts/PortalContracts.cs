using System.ComponentModel.DataAnnotations;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Contracts;

public sealed record MessageResponse(string Message);

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(255)] public string Email { get; init; } = "";
    [Required, EmailAddress, MaxLength(255)] public string ConfirmEmail { get; init; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; init; } = "";
    [Required] public string ConfirmPassword { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string LastName { get; init; } = "";
    [MaxLength(32)] public string CountryCode { get; init; } = "+966";
    [Required, MinLength(6), MaxLength(40)] public string Phone { get; init; } = "";
    [Required, MinLength(2), MaxLength(100)] public string Country { get; init; } = "";
    public bool AcceptedTerms { get; init; }
}

public sealed class LoginRequest
{
    [Required] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
}

public sealed class PasswordUpdateRequest
{
    [Required] public string CurrentPassword { get; init; } = "";
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; init; } = "";
    [Required] public string ConfirmPassword { get; init; } = "";
}

public sealed class ProfileUpdateRequest
{
    [Required, MinLength(1), MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string LastName { get; init; } = "";
    [MaxLength(32)] public string CountryCode { get; init; } = "";
    [MaxLength(40)] public string Phone { get; init; } = "";
    [MaxLength(100)] public string Country { get; init; } = "";
    [MaxLength(100)] public string City { get; init; } = "";
    [MaxLength(150)] public string Title { get; init; } = "";
    [MaxLength(2000)] public string About { get; init; } = "";
}

public sealed record UserResponse(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string CountryCode,
    string Phone,
    string Country,
    string City,
    string Title,
    string About,
    string Role,
    string? ResumeName,
    DateTime CreatedAt);

public sealed record AuthResponse(string AccessToken, string TokenType, UserResponse User);

public sealed record JobResponse(
    int Id,
    string Title,
    string Division,
    string Country,
    string City,
    string JobFunction,
    string CareerLevel,
    string EmploymentType,
    string Summary,
    string Description,
    string Requirements,
    bool IsOpen,
    bool IsFeatured,
    DateTime PostedAt,
    DateTime? ExpiresAt);

public sealed record JobListResponse(
    IReadOnlyList<JobResponse> Items,
    int Total,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Filters);

public sealed record ApplicationResponse(
    int Id,
    string ApplicationCode,
    string Status,
    DateTime AppliedAt,
    JobResponse Job);

public sealed record DashboardActivity(string Label, DateTime Date);

public sealed record DashboardResponse(
    int Applications,
    int Interviews,
    int OpenJobs,
    int ProfileComplete,
    IReadOnlyList<DashboardActivity> RecentActivity);

public sealed record AdminSummaryResponse(int Users, int Candidates, int Admins, int OpenJobs, int Applications);

public sealed record AdminJobOptionsResponse(
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> Divisions,
    IReadOnlyList<string> JobFunctions,
    IReadOnlyList<string> CareerLevels);

public sealed record AdminAuditLogResponse(
    int Id,
    string Action,
    string EntityType,
    string? EntityId,
    string Details,
    DateTime CreatedAt,
    int AdminUserId,
    string AdminName,
    string AdminEmail);

public class AdminJobCreateRequest
{
    [Required, MinLength(1), MaxLength(180)] public string Title { get; init; } = "";
    [Required, MinLength(1), MaxLength(120)] public string Division { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string Country { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string City { get; init; } = "";
    [Required, MinLength(1), MaxLength(120)] public string JobFunction { get; init; } = "";
    [Required] public string CareerLevel { get; init; } = "";
    [Required] public string EmploymentType { get; init; } = "";
    [Required, MinLength(1), MaxLength(2000)] public string Summary { get; init; } = "";
    [Required, MinLength(1), MaxLength(10000)] public string Description { get; init; } = "";
    [Required, MinLength(1), MaxLength(10000)] public string Requirements { get; init; } = "";
    public bool IsOpen { get; init; } = true;
    public bool IsFeatured { get; init; }
    [Required] public DateTime? ExpiresAt { get; init; }
}

public sealed class AdminJobUpdateRequest
{
    [MinLength(1), MaxLength(180)] public string? Title { get; init; }
    [MinLength(1), MaxLength(120)] public string? Division { get; init; }
    [MinLength(1), MaxLength(100)] public string? Country { get; init; }
    [MinLength(1), MaxLength(100)] public string? City { get; init; }
    [MinLength(1), MaxLength(120)] public string? JobFunction { get; init; }
    public string? CareerLevel { get; init; }
    public string? EmploymentType { get; init; }
    [MinLength(1), MaxLength(2000)] public string? Summary { get; init; }
    [MinLength(1), MaxLength(10000)] public string? Description { get; init; }
    [MinLength(1), MaxLength(10000)] public string? Requirements { get; init; }
    public bool? IsOpen { get; init; }
    public bool? IsFeatured { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed class ApplicationStatusUpdateRequest
{
    [Required] public string Status { get; init; } = "";
}

public sealed record AdminCandidateResponse(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string Phone,
    string Country,
    string City,
    string Title,
    string? ResumeName,
    string? ResumeUrl,
    int ApplicationCount,
    DateTime CreatedAt);

public sealed record AdminApplicationResponse(
    int Id,
    string ApplicationCode,
    string Status,
    DateTime AppliedAt,
    AdminCandidateResponse Candidate,
    JobResponse Job);

public sealed record NotificationResponse(
    int Id,
    string Kind,
    string Title,
    string Message,
    string? Link,
    bool IsRead,
    DateTime CreatedAt);

public sealed record NotificationCountResponse(int Unread);

public sealed record PreferenceResponse(
    bool EmailUpdates,
    bool JobAlerts,
    bool Marketing,
    string Language,
    string Theme,
    DateTime UpdatedAt);

public sealed class PreferenceUpdateRequest
{
    public bool EmailUpdates { get; init; }
    public bool JobAlerts { get; init; }
    public bool Marketing { get; init; }
    [Required] public string Language { get; init; } = "English";
    [Required] public string Theme { get; init; } = "light";
}

public static class PortalMappings
{
    public static UserResponse ToResponse(this User user) => new(
        user.Id, user.Email, user.FirstName, user.LastName, user.CountryCode, user.Phone,
        user.Country, user.City, user.Title, user.About, user.Role, user.ResumeName, user.CreatedAt);

    public static JobResponse ToResponse(this Job job) => new(
        job.Id, job.Title, job.Division, job.Country, job.City, job.JobFunction,
        job.CareerLevel, job.EmploymentType, job.Summary, job.Description, job.Requirements,
        job.IsOpen, job.IsFeatured, job.PostedAt, job.ExpiresAt);

    public static ApplicationResponse ToResponse(this Application application) => new(
        application.Id, $"APP-{application.Id:0000}", application.Status,
        application.AppliedAt, application.Job.ToResponse());

    public static NotificationResponse ToResponse(this Notification notification) => new(
        notification.Id, notification.Kind, notification.Title, notification.Message,
        notification.Link, notification.IsRead, notification.CreatedAt);

    public static PreferenceResponse ToResponse(this UserPreference preference) => new(
        preference.EmailUpdates, preference.JobAlerts, preference.Marketing,
        preference.Language, preference.Theme, preference.UpdatedAt);
}

public static class PortalValues
{
    public static readonly HashSet<string> CareerLevels = ["Entry level", "Mid-level", "Senior"];
    public static readonly HashSet<string> EmploymentTypes = ["Full-time", "Part-time", "Contract", "Remote"];
    public static readonly HashSet<string> ApplicationStatuses =
        ["Under Review", "Interview", "Shortlisted", "Rejected", "Hired", "Withdrawn"];
    public static readonly HashSet<string> Languages = ["English", "Arabic"];
    public static readonly HashSet<string> Themes = ["light", "dark"];
}
