namespace CandidatePortal.Api.Configuration;

public sealed class PortalOptions
{
    public string AppName { get; init; } = "PBICareerPosting API";
    public required string SqlServerConnectionString { get; init; }
    public required string SecretKey { get; init; }
    public int AccessTokenMinutes { get; init; } = 480;
    public string[] FrontendOrigins { get; init; } = ["http://localhost:5173"];
    public bool AutoCreateSchema { get; init; }
    public bool SeedDemoData { get; init; }
    public string BootstrapAdminEmail { get; init; } = "";
    public string BootstrapAdminPassword { get; init; } = "";
    public string BootstrapHrAdminEmail { get; init; } = "";
    public string BootstrapHrAdminPassword { get; init; } = "";
    public string GoogleAuthClientId { get; init; } = "";
    public string GoogleAuthClientSecret { get; init; } = "";
    public string MicrosoftAuthClientId { get; init; } = "";
    public string MicrosoftAuthClientSecret { get; init; } = "";
    public string EmailDeliveryMode { get; init; } = "development";
    public string EmailSenderAddress { get; init; } = "dev-no-reply@candidateportal.local";
    public string EmailSenderName { get; init; } = "PBICareerPosting";
    public string SmtpHost { get; init; } = "";
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = "";
    public string SmtpTenantId { get; init; } = "";
    public string SmtpClientId { get; init; } = "";
    public string SmtpClientSecret { get; init; } = "";
    public bool SmtpEnableSsl { get; init; } = true;
    public int SmtpTimeoutSeconds { get; init; } = 30;
    public int EmailVerificationMinutes { get; init; } = 10;
    public int EmailVerificationResendSeconds { get; init; } = 60;
    public int EmailVerificationMaxAttempts { get; init; } = 5;
    public int PasswordResetMinutes { get; init; } = 10;
    public int PasswordResetResendSeconds { get; init; } = 60;
    public int PasswordResetMaxAttempts { get; init; } = 5;
    public string PrivacyPolicyVersion { get; init; } = "1.0";
    public string PrivacyEffectiveDate { get; init; } = "2026-09-08";
    public string PrivacyContactEmail { get; init; } = "privacy@candidateportal.local";
    public bool ExposeDevelopmentVerificationCode { get; init; } = true;
    public string StorageProvider { get; init; } = "local";
    public string LocalStoragePath { get; init; } = "../storage";
    public string SharePointTenantId { get; init; } = "";
    public string SharePointClientId { get; init; } = "";
    public string SharePointClientSecret { get; init; } = "";
    public string SharePointSiteUrl { get; init; } = "";
    public string SharePointSiteId { get; init; } = "";
    public string SharePointDriveId { get; init; } = "";
    public string SharePointCandidatesList { get; init; } = "Candidates";
    public string SharePointJobsList { get; init; } = "Jobs";
    public string SharePointApplicationsList { get; init; } = "Applications";
    public string SharePointRecruitmentRequestsList { get; init; } = "Recruitment Requests";
    public string SharePointCooperativeTrainingList { get; init; } = "Cooperative Training Requests";
    public string SharePointCooperativeTrainingDocumentsLibrary { get; init; } = "Cooperative Training Documents";
    public string SharePointResumesLibrary { get; init; } = "Candidate Resume Files";
    public double SharePointTimeoutSeconds { get; init; } = 30;
    public bool SharePointSyncEnabled { get; init; }

    public bool GoogleAuthEnabled =>
        !string.IsNullOrWhiteSpace(GoogleAuthClientId) &&
        !string.IsNullOrWhiteSpace(GoogleAuthClientSecret);

    public bool MicrosoftAuthEnabled =>
        !string.IsNullOrWhiteSpace(MicrosoftAuthClientId) &&
        !string.IsNullOrWhiteSpace(MicrosoftAuthClientSecret);

    public bool SharePointConfigured =>
        !string.IsNullOrWhiteSpace(SharePointTenantId) &&
        !string.IsNullOrWhiteSpace(SharePointClientId) &&
        !string.IsNullOrWhiteSpace(SharePointClientSecret) &&
        (!string.IsNullOrWhiteSpace(SharePointSiteId) || !string.IsNullOrWhiteSpace(SharePointSiteUrl));

    public bool SmtpConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost) &&
        SmtpPort > 0 &&
        !string.IsNullOrWhiteSpace(SmtpUsername) &&
        !string.IsNullOrWhiteSpace(SmtpTenantId) &&
        !string.IsNullOrWhiteSpace(SmtpClientId) &&
        !string.IsNullOrWhiteSpace(SmtpClientSecret) &&
        !string.IsNullOrWhiteSpace(EmailSenderAddress);

    public static PortalOptions FromConfiguration(IConfiguration configuration)
    {
        static bool Flag(IConfiguration config, string name, bool fallback = false) =>
            bool.TryParse(config[name], out var value) ? value : fallback;
        static int Number(IConfiguration config, string name, int fallback) =>
            int.TryParse(config[name], out var value) ? value : fallback;
        static double DecimalNumber(IConfiguration config, string name, double fallback) =>
            double.TryParse(config[name], out var value) ? value : fallback;
        var sqlServerConnectionString = configuration["SQLSERVER_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(sqlServerConnectionString))
        {
            sqlServerConnectionString =
                "Server=DESKTOP-NCLK3BN;Database=CandidatePortal;Integrated Security=True;Encrypt=False;TrustServerCertificate=True";
        }
        var frontendOrigins = (configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (frontendOrigins.Length == 0)
        {
            frontendOrigins = ["http://localhost:5173"];
        }

        return new PortalOptions
        {
            SqlServerConnectionString = sqlServerConnectionString,
            SecretKey = configuration["SECRET_KEY"] ?? "development-only-change-me",
            AccessTokenMinutes = Number(configuration, "ACCESS_TOKEN_MINUTES", 480),
            FrontendOrigins = frontendOrigins,
            AutoCreateSchema = Flag(configuration, "AUTO_CREATE_SCHEMA"),
            SeedDemoData = Flag(configuration, "SEED_DEMO_DATA"),
            BootstrapAdminEmail = (configuration["BOOTSTRAP_ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant(),
            BootstrapAdminPassword = configuration["BOOTSTRAP_ADMIN_PASSWORD"] ?? "",
            BootstrapHrAdminEmail = (configuration["BOOTSTRAP_HR_ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant(),
            BootstrapHrAdminPassword = configuration["BOOTSTRAP_HR_ADMIN_PASSWORD"] ?? "",
            GoogleAuthClientId = configuration["GOOGLE_AUTH_CLIENT_ID"] ?? "",
            GoogleAuthClientSecret = configuration["GOOGLE_AUTH_CLIENT_SECRET"] ?? "",
            MicrosoftAuthClientId = configuration["MICROSOFT_AUTH_CLIENT_ID"] ?? "",
            MicrosoftAuthClientSecret = configuration["MICROSOFT_AUTH_CLIENT_SECRET"] ?? "",
            EmailDeliveryMode = (configuration["EMAIL_DELIVERY_MODE"] ?? "development").Trim().ToLowerInvariant(),
            EmailSenderAddress = (configuration["EMAIL_SENDER_ADDRESS"] ?? "dev-no-reply@candidateportal.local").Trim(),
            EmailSenderName = (configuration["EMAIL_SENDER_NAME"] ?? "PBICareerPosting").Trim(),
            SmtpHost = (configuration["SMTP_HOST"] ?? "").Trim(),
            SmtpPort = Math.Clamp(Number(configuration, "SMTP_PORT", 587), 1, 65535),
            SmtpUsername = (configuration["SMTP_USERNAME"] ?? "").Trim(),
            SmtpTenantId = (configuration["SMTP_TENANT_ID"] ?? "").Trim(),
            SmtpClientId = (configuration["SMTP_CLIENT_ID"] ?? "").Trim(),
            SmtpClientSecret = configuration["SMTP_CLIENT_SECRET"] ?? "",
            SmtpEnableSsl = Flag(configuration, "SMTP_ENABLE_SSL", true),
            SmtpTimeoutSeconds = Math.Clamp(Number(configuration, "SMTP_TIMEOUT_SECONDS", 30), 5, 120),
            EmailVerificationMinutes = Math.Clamp(Number(configuration, "EMAIL_VERIFICATION_MINUTES", 10), 5, 60),
            EmailVerificationResendSeconds = Math.Clamp(Number(configuration, "EMAIL_VERIFICATION_RESEND_SECONDS", 60), 30, 300),
            EmailVerificationMaxAttempts = Math.Clamp(Number(configuration, "EMAIL_VERIFICATION_MAX_ATTEMPTS", 5), 3, 10),
            PasswordResetMinutes = Math.Clamp(Number(configuration, "PASSWORD_RESET_MINUTES", 10), 5, 60),
            PasswordResetResendSeconds = Math.Clamp(Number(configuration, "PASSWORD_RESET_RESEND_SECONDS", 60), 30, 300),
            PasswordResetMaxAttempts = Math.Clamp(Number(configuration, "PASSWORD_RESET_MAX_ATTEMPTS", 5), 3, 10),
            PrivacyPolicyVersion = (configuration["PRIVACY_POLICY_VERSION"] ?? "1.0").Trim(),
            PrivacyEffectiveDate = (configuration["PRIVACY_EFFECTIVE_DATE"] ?? "2026-09-08").Trim(),
            PrivacyContactEmail = (configuration["PRIVACY_CONTACT_EMAIL"] ?? "privacy@candidateportal.local").Trim(),
            ExposeDevelopmentVerificationCode = Flag(configuration, "EMAIL_EXPOSE_DEVELOPMENT_CODE", true),
            StorageProvider = configuration["STORAGE_PROVIDER"] ?? "local",
            LocalStoragePath = configuration["LOCAL_STORAGE_PATH"] ?? "../storage",
            SharePointTenantId = configuration["SHAREPOINT_TENANT_ID"] ?? "",
            SharePointClientId = configuration["SHAREPOINT_CLIENT_ID"] ?? "",
            SharePointClientSecret = configuration["SHAREPOINT_CLIENT_SECRET"] ?? "",
            SharePointSiteUrl = configuration["SHAREPOINT_SITE_URL"] ?? "",
            SharePointSiteId = configuration["SHAREPOINT_SITE_ID"] ?? "",
            SharePointDriveId = configuration["SHAREPOINT_DRIVE_ID"] ?? "",
            SharePointCandidatesList = configuration["SHAREPOINT_CANDIDATES_LIST"] ?? "Candidates",
            SharePointJobsList = configuration["SHAREPOINT_JOBS_LIST"] ?? "Jobs",
            SharePointApplicationsList = configuration["SHAREPOINT_APPLICATIONS_LIST"] ?? "Applications",
            SharePointRecruitmentRequestsList = configuration["SHAREPOINT_RECRUITMENT_REQUESTS_LIST"] ?? "Recruitment Requests",
            SharePointCooperativeTrainingList = configuration["SHAREPOINT_COOPERATIVE_TRAINING_LIST"] ?? "Cooperative Training Requests",
            SharePointCooperativeTrainingDocumentsLibrary = configuration["SHAREPOINT_COOPERATIVE_TRAINING_DOCUMENTS_LIBRARY"] ?? "Cooperative Training Documents",
            SharePointResumesLibrary = configuration["SHAREPOINT_RESUMES_LIBRARY"] ?? "Candidate Resume Files",
            SharePointTimeoutSeconds = DecimalNumber(configuration, "SHAREPOINT_TIMEOUT_SECONDS", 30),
            SharePointSyncEnabled = Flag(configuration, "SHAREPOINT_SYNC_ENABLED"),
        };
    }
}
