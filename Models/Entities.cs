namespace CandidatePortal.Api.Models;

public static class PortalClock
{
    public static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}

public sealed class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string CountryCode { get; set; } = "+966";
    public string Phone { get; set; } = "";
    public string Country { get; set; } = "Saudi Arabia";
    public string City { get; set; } = "Riyadh";
    public string Title { get; set; } = "Candidate";
    public string About { get; set; } = "";
    public string Role { get; set; } = "candidate";
    public string? ResumeName { get; set; }
    public string? ResumePath { get; set; }
    public DateTime CreatedAt { get; set; } = PortalClock.UtcNow();
    public List<Application> Applications { get; set; } = [];
    public List<AuthSession> AuthSessions { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public UserPreference? Preferences { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public sealed class Job
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Division { get; set; } = "";
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
    public string JobFunction { get; set; } = "";
    public string CareerLevel { get; set; } = "";
    public string EmploymentType { get; set; } = "Full-time";
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string Requirements { get; set; } = "";
    public bool IsOpen { get; set; } = true;
    public bool IsFeatured { get; set; }
    public DateTime PostedAt { get; set; } = PortalClock.UtcNow();
    public DateTime? ExpiresAt { get; set; }
    public List<Application> Applications { get; set; } = [];
}

public sealed class AuditLog
{
    public int Id { get; set; }
    public int AdminUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string Details { get; set; } = "";
    public DateTime CreatedAt { get; set; } = PortalClock.UtcNow();
    public User AdminUser { get; set; } = null!;
}

public sealed class Application
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int JobId { get; set; }
    public string Status { get; set; } = "Under Review";
    public DateTime AppliedAt { get; set; } = PortalClock.UtcNow();
    public User User { get; set; } = null!;
    public Job Job { get; set; } = null!;
}

public sealed class AuthSession
{
    public string Id { get; set; } = "";
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = PortalClock.UtcNow();
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public User User { get; set; } = null!;
}

public sealed class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "system";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = PortalClock.UtcNow();
    public User User { get; set; } = null!;
}

public sealed class UserPreference
{
    public int UserId { get; set; }
    public bool EmailUpdates { get; set; } = true;
    public bool JobAlerts { get; set; } = true;
    public bool Marketing { get; set; }
    public string Language { get; set; } = "English";
    public string Theme { get; set; } = "light";
    public DateTime UpdatedAt { get; set; } = PortalClock.UtcNow();
    public User User { get; set; } = null!;
}
