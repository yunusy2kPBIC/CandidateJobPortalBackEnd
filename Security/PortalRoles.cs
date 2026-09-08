namespace CandidatePortal.Api.Security;

public static class PortalRoles
{
    public const string Candidate = "candidate";
    public const string Student = "student";
    public const string Administrator = "admin";
    public const string HrAdministrator = "hr_admin";

    public const string RecruitmentAdministrators = Administrator + "," + HrAdministrator;

    public static string SharePointName(string role) => role switch
    {
        Candidate => "Candidate",
        Student => "Student",
        Administrator => "Admin",
        HrAdministrator => "HR Admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported portal role"),
    };
}
