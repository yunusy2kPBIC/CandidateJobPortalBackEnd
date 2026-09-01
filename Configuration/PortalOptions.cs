namespace CandidatePortal.Api.Configuration;

public sealed class PortalOptions
{
    public string AppName { get; init; } = "PBICareerPosting API";
    public string DatabaseProvider { get; init; } = "postgresql";
    public required string DatabaseUrl { get; init; }
    public required string SecretKey { get; init; }
    public int AccessTokenMinutes { get; init; } = 480;
    //public string[] FrontendOrigins { get; init; } = ["http://localhost:5173"];
    public string[] FrontendOrigins { get; init; } = ["http://localhost:5174"];
    public bool AutoCreateSchema { get; init; }
    public bool SeedDemoData { get; init; }
    public string BootstrapAdminEmail { get; init; } = "";
    public string BootstrapAdminPassword { get; init; } = "";
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

    public bool SharePointConfigured =>
        !string.IsNullOrWhiteSpace(SharePointTenantId) &&
        !string.IsNullOrWhiteSpace(SharePointClientId) &&
        !string.IsNullOrWhiteSpace(SharePointClientSecret) &&
        (!string.IsNullOrWhiteSpace(SharePointSiteId) || !string.IsNullOrWhiteSpace(SharePointSiteUrl));

    public static PortalOptions FromConfiguration(IConfiguration configuration)
    {
        static bool Flag(IConfiguration config, string name, bool fallback = false) =>
            bool.TryParse(config[name], out var value) ? value : fallback;
        static int Number(IConfiguration config, string name, int fallback) =>
            int.TryParse(config[name], out var value) ? value : fallback;
        static double DecimalNumber(IConfiguration config, string name, double fallback) =>
            double.TryParse(config[name], out var value) ? value : fallback;

        var databaseProvider = (configuration["DATABASE_PROVIDER"] ?? "postgresql")
            .Trim().ToLowerInvariant();
        var databaseUrl = databaseProvider == "sqlserver"
            ? configuration["SQLSERVER_CONNECTION_STRING"] ?? configuration["DATABASE_URL"]
            : configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            databaseUrl = databaseProvider == "sqlserver"
                ? "Server=DESKTOP-NCLK3BN;Database=CandidatePortal;Integrated Security=True;Encrypt=False;TrustServerCertificate=True"
                : "Host=localhost;Port=5432;Database=candidate_portal;Username=candidate_portal;Password=CHANGE_ME";
        }

        return new PortalOptions
        {
            DatabaseProvider = databaseProvider,
            DatabaseUrl = databaseUrl,
            SecretKey = configuration["SECRET_KEY"] ?? "development-only-change-me",
            AccessTokenMinutes = Number(configuration, "ACCESS_TOKEN_MINUTES", 480),
            //FrontendOrigins = (configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173")
            FrontendOrigins = (configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5174" +
            "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            AutoCreateSchema = Flag(configuration, "AUTO_CREATE_SCHEMA"),
            SeedDemoData = Flag(configuration, "SEED_DEMO_DATA"),
            BootstrapAdminEmail = (configuration["BOOTSTRAP_ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant(),
            BootstrapAdminPassword = configuration["BOOTSTRAP_ADMIN_PASSWORD"] ?? "",
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
