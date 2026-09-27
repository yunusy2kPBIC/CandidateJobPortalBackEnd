using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Services;

public sealed class DatabaseBootstrapper(
    PortalDbContext database,
    PortalOptions options,
    PasswordHasher passwordHasher)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (options.AutoCreateSchema)
        {
            await database.Database.EnsureCreatedAsync(cancellationToken);
        }
        await EnsureSchemaEvolutionAsync(cancellationToken);

        if (options.SeedDemoData)
        {
            await SeedDemoDataAsync(cancellationToken);
        }
        await EnsureLookupDataAsync(cancellationToken);
        await BootstrapAdministrativeUserAsync(
            options.BootstrapAdminEmail,
            options.BootstrapAdminPassword,
            "BOOTSTRAP_ADMIN_EMAIL",
            "BOOTSTRAP_ADMIN_PASSWORD",
            PortalRoles.Administrator,
            "Staging",
            "Administrator",
            "PBICareerPosting Administrator",
            "PBICareerPosting administrator account.",
            cancellationToken);
        await BootstrapAdministrativeUserAsync(
            options.BootstrapHrAdminEmail,
            options.BootstrapHrAdminPassword,
            "BOOTSTRAP_HR_ADMIN_EMAIL",
            "BOOTSTRAP_HR_ADMIN_PASSWORD",
            PortalRoles.HrAdministrator,
            "HR",
            "Administrator",
            "PBICareerPosting HR Administrator",
            "PBICareerPosting recruitment administration account.",
            cancellationToken);
    }

    private async Task EnsureSchemaEvolutionAsync(CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('jobs', 'expires_at') IS NULL ALTER TABLE jobs ADD expires_at datetime2 NULL;",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('jobs', 'is_published') IS NULL ALTER TABLE jobs ADD is_published bit NOT NULL CONSTRAINT DF_jobs_is_published DEFAULT 1;",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('users', 'nationality') IS NULL ALTER TABLE users ADD nationality nvarchar(50) NOT NULL CONSTRAINT DF_users_nationality DEFAULT '';",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('users', 'gender') IS NULL ALTER TABLE users ADD gender nvarchar(20) NOT NULL CONSTRAINT DF_users_gender DEFAULT '';",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('users', 'is_email_verified') IS NULL ALTER TABLE users ADD is_email_verified bit NOT NULL CONSTRAINT DF_users_is_email_verified DEFAULT 1;",
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'sharepoint_outbox', N'U') IS NULL
            BEGIN
                CREATE TABLE sharepoint_outbox (
                    id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_sharepoint_outbox PRIMARY KEY,
                    operation nvarchar(50) NOT NULL,
                    entity_id int NOT NULL,
                    file_name nvarchar(255) NULL,
                    content_type nvarchar(150) NULL,
                    content varbinary(max) NULL,
                    attempts int NOT NULL CONSTRAINT DF_sharepoint_outbox_attempts DEFAULT 0,
                    next_attempt_at datetime2 NOT NULL,
                    last_error nvarchar(2000) NULL,
                    lock_token nvarchar(64) NULL,
                    locked_until datetime2 NULL,
                    created_at datetime2 NOT NULL,
                    processed_at datetime2 NULL
                );
                CREATE INDEX ix_sharepoint_outbox_pending
                    ON sharepoint_outbox(processed_at, next_attempt_at);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'audit_logs', N'U') IS NULL
            BEGIN
                CREATE TABLE audit_logs (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_audit_logs PRIMARY KEY,
                    admin_user_id int NOT NULL,
                    action nvarchar(50) NOT NULL,
                    entity_type nvarchar(80) NOT NULL,
                    entity_id nvarchar(100) NULL,
                    details nvarchar(max) NOT NULL,
                    created_at datetime2 NOT NULL,
                    CONSTRAINT FK_audit_logs_users_admin_user_id
                        FOREIGN KEY (admin_user_id) REFERENCES users(id) ON DELETE NO ACTION
                );
                CREATE INDEX IX_audit_logs_admin_user_id ON audit_logs(admin_user_id);
                CREATE INDEX IX_audit_logs_created_at ON audit_logs(created_at);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'external_logins', N'U') IS NULL
            BEGIN
                CREATE TABLE external_logins (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_external_logins PRIMARY KEY,
                    user_id int NOT NULL,
                    provider nvarchar(30) NOT NULL,
                    provider_user_id nvarchar(255) NOT NULL,
                    email nvarchar(255) NOT NULL,
                    created_at datetime2 NOT NULL,
                    CONSTRAINT FK_external_logins_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                    CONSTRAINT uq_external_login UNIQUE (provider, provider_user_id)
                );
                CREATE INDEX IX_external_logins_user_id ON external_logins(user_id);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'external_auth_codes', N'U') IS NULL
            BEGIN
                CREATE TABLE external_auth_codes (
                    code_hash nvarchar(64) NOT NULL CONSTRAINT PK_external_auth_codes PRIMARY KEY,
                    user_id int NOT NULL,
                    created_at datetime2 NOT NULL,
                    expires_at datetime2 NOT NULL,
                    CONSTRAINT FK_external_auth_codes_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                );
                CREATE INDEX IX_external_auth_codes_user_id ON external_auth_codes(user_id);
                CREATE INDEX IX_external_auth_codes_expires_at ON external_auth_codes(expires_at);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'lookup_values', N'U') IS NULL
            BEGIN
                CREATE TABLE lookup_values (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_lookup_values PRIMARY KEY,
                    category nvarchar(50) NOT NULL,
                    value nvarchar(150) NOT NULL,
                    parent_value nvarchar(150) NULL,
                    sort_order int NOT NULL CONSTRAINT DF_lookup_values_sort_order DEFAULT 0,
                    is_active bit NOT NULL CONSTRAINT DF_lookup_values_is_active DEFAULT 1,
                    CONSTRAINT uq_lookup_value UNIQUE (category, value, parent_value)
                );
                CREATE INDEX ix_lookup_category_active_order ON lookup_values(category, is_active, sort_order);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'email_verifications', N'U') IS NULL
            BEGIN
                CREATE TABLE email_verifications (
                    user_id int NOT NULL CONSTRAINT PK_email_verifications PRIMARY KEY,
                    code_hash nvarchar(64) NOT NULL,
                    attempt_count int NOT NULL CONSTRAINT DF_email_verifications_attempt_count DEFAULT 0,
                    created_at datetime2 NOT NULL,
                    expires_at datetime2 NOT NULL,
                    resend_available_at datetime2 NOT NULL,
                    consumed_at datetime2 NULL,
                    CONSTRAINT FK_email_verifications_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                );
                CREATE INDEX IX_email_verifications_expires_at ON email_verifications(expires_at);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'password_resets', N'U') IS NULL
            BEGIN
                CREATE TABLE password_resets (
                    user_id int NOT NULL CONSTRAINT PK_password_resets PRIMARY KEY,
                    code_hash nvarchar(64) NOT NULL,
                    attempt_count int NOT NULL CONSTRAINT DF_password_resets_attempt_count DEFAULT 0,
                    created_at datetime2 NOT NULL,
                    expires_at datetime2 NOT NULL,
                    resend_available_at datetime2 NOT NULL,
                    consumed_at datetime2 NULL,
                    CONSTRAINT FK_password_resets_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                );
                CREATE INDEX IX_password_resets_expires_at ON password_resets(expires_at);
            END
            """,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'user_consents', N'U') IS NULL
            BEGIN
                CREATE TABLE user_consents (
                    id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_user_consents PRIMARY KEY,
                    user_id int NOT NULL,
                    document_type nvarchar(50) NOT NULL,
                    document_version nvarchar(50) NOT NULL,
                    accepted_at datetime2 NOT NULL,
                    ip_address nvarchar(64) NOT NULL,
                    user_agent nvarchar(500) NOT NULL,
                    CONSTRAINT FK_user_consents_users_user_id
                        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                    CONSTRAINT uq_user_consent_document_version
                        UNIQUE (user_id, document_type, document_version)
                );
            END
            """,
            cancellationToken);
    }

    private async Task EnsureLookupDataAsync(CancellationToken cancellationToken)
    {
        var existingRows = await database.LookupValues.ToListAsync(cancellationToken);
        var existing = existingRows
            .Select(value => LookupKey(value.Category, value.Value, value.ParentValue))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingCategories = existingRows
            .Select(value => value.Category.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void Add(string category, string value, string? parentValue, int sortOrder)
        {
            value = value.Trim();
            parentValue = string.IsNullOrWhiteSpace(parentValue) ? null : parentValue.Trim();
            if (value.Length == 0 || !existing.Add(LookupKey(category, value, parentValue))) return;
            database.LookupValues.Add(new LookupValue
            {
                Category = category,
                Value = value,
                ParentValue = parentValue,
                SortOrder = sortOrder,
                IsActive = true,
            });
        }

        var locations = new (string Country, string[] Cities)[]
        {
            ("Saudi Arabia", ["Riyadh", "Jeddah", "Dammam", "Al Khobar", "Madinah"]),
            ("United Arab Emirates", ["Abu Dhabi", "Dubai", "Sharjah", "Ajman"]),
            ("Bahrain", ["Manama", "Riffa", "Muharraq"]),
            ("Kuwait", ["Kuwait City", "Al Ahmadi", "Hawalli"]),
            ("United States", ["New York", "Los Angeles", "Chicago", "Houston"]),
        };
        if (!existingCategories.Contains(LookupCategories.Country) ||
            !existingCategories.Contains(LookupCategories.City))
        {
            for (var countryIndex = 0; countryIndex < locations.Length; countryIndex++)
            {
                var location = locations[countryIndex];
                if (!existingCategories.Contains(LookupCategories.Country))
                    Add(LookupCategories.Country, location.Country, null, (countryIndex + 1) * 10);
                if (!existingCategories.Contains(LookupCategories.City))
                    for (var cityIndex = 0; cityIndex < location.Cities.Length; cityIndex++)
                        Add(LookupCategories.City, location.Cities[cityIndex], location.Country, (cityIndex + 1) * 10);
            }
        }

        var divisions = new[]
        {
            "Executive Management", "Finance", "Human Resource", "Information Technology", "Legal",
            "Manufacturing", "Marketing", "QHSSE", "Sales", "Supply Chain",
        };
        var jobFunctions = new[]
        {
            "Engineering", "Business Analysis", "Design", "Project Management", "Data & Analytics",
            "Human Resources", "Information Security", "Finance", "Customer Experience", "Operations",
        };
        var careerLevels = new[] { "Entry level", "Mid-level", "Senior" };
        if (!existingCategories.Contains(LookupCategories.Division))
            for (var index = 0; index < divisions.Length; index++)
                Add(LookupCategories.Division, divisions[index], null, (index + 1) * 10);
        if (!existingCategories.Contains(LookupCategories.JobFunction))
            for (var index = 0; index < jobFunctions.Length; index++)
                Add(LookupCategories.JobFunction, jobFunctions[index], null, (index + 1) * 10);
        if (!existingCategories.Contains(LookupCategories.CareerLevel))
            for (var index = 0; index < careerLevels.Length; index++)
                Add(LookupCategories.CareerLevel, careerLevels[index], null, (index + 1) * 10);

        await database.SaveChangesAsync(cancellationToken);
    }

    private static string LookupKey(string category, string value, string? parentValue) =>
        $"{category.Trim()}\u001f{value.Trim()}\u001f{parentValue?.Trim() ?? ""}";

    private async Task BootstrapAdministrativeUserAsync(
        string email,
        string password,
        string emailSetting,
        string passwordSetting,
        string role,
        string firstName,
        string lastName,
        string title,
        string about,
        CancellationToken cancellationToken)
    {
        if (email.Length == 0 && password.Length == 0)
        {
            return;
        }
        if (email.Length == 0 || password.Length == 0)
        {
            throw new InvalidOperationException($"{emailSetting} and {passwordSetting} must be provided together");
        }
        if (password.Length < 12)
        {
            throw new InvalidOperationException($"{passwordSetting} must contain at least 12 characters");
        }

        var existing = await database.Users.SingleOrDefaultAsync(
            user => user.Email == email, cancellationToken);
        if (existing is not null)
        {
            if (existing.Role != role)
            {
                throw new InvalidOperationException($"{emailSetting} already belongs to an account with a different role");
            }
            return;
        }

        var administrativeUser = new User
        {
            Email = email,
            PasswordHash = passwordHasher.Hash(password),
            FirstName = firstName,
            LastName = lastName,
            CountryCode = "+966",
            Phone = "",
            Country = "Saudi Arabia",
            City = "Riyadh",
            Title = title,
            About = about,
            Role = role,
        };
        administrativeUser.Preferences = new UserPreference();
        database.Users.Add(administrativeUser);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDemoDataAsync(CancellationToken cancellationToken)
    {
        var now = PortalClock.UtcNow();
        if (!await database.Jobs.AnyAsync(cancellationToken))
        {
            var jobs = new[]
            {
                ("Senior React Developer", "IT Division", "Saudi Arabia", "Riyadh", "Engineering", "Senior", true),
                ("Business Analyst", "Business Division", "Saudi Arabia", "Jeddah", "Business Analysis", "Mid-level", true),
                ("UI/UX Designer", "Marketing Division", "Remote", "Remote", "Design", "Mid-level", true),
                ("Project Manager", "Transformation Office", "Saudi Arabia", "Riyadh", "Project Management", "Senior", false),
                ("Data Analyst", "Strategy Division", "Saudi Arabia", "Dammam", "Data & Analytics", "Mid-level", false),
                ("Python API Engineer", "IT Division", "United Arab Emirates", "Dubai", "Engineering", "Mid-level", true),
                ("Talent Acquisition Partner", "People Division", "Saudi Arabia", "Riyadh", "Human Resources", "Mid-level", false),
                ("Cybersecurity Specialist", "IT Division", "Saudi Arabia", "Jeddah", "Information Security", "Senior", true),
                ("Finance Operations Lead", "Finance Division", "Bahrain", "Manama", "Finance", "Senior", false),
                ("Graduate Software Engineer", "IT Division", "Saudi Arabia", "Riyadh", "Engineering", "Entry level", true),
                ("Customer Experience Manager", "Commercial Division", "Kuwait", "Kuwait City", "Customer Experience", "Senior", false),
                ("Supply Chain Planner", "Operations Division", "Saudi Arabia", "Dammam", "Operations", "Mid-level", false),
            };
            for (var index = 0; index < jobs.Length; index++)
            {
                var value = jobs[index];
                database.Jobs.Add(new Job
                {
                    Title = value.Item1,
                    Division = value.Item2,
                    Country = value.Item3,
                    City = value.Item4,
                    JobFunction = value.Item5,
                    CareerLevel = value.Item6,
                    EmploymentType = index == 2 ? "Remote" : "Full-time",
                    Summary = $"Join our team as a {value.Item1}.",
                    Description = $"We are looking for a {value.Item1} to join our growing {value.Item2}.",
                    Requirements = "Relevant experience\nStrong communication and collaboration skills",
                    IsFeatured = value.Item7,
                    PostedAt = now.AddDays(-index),
                    ExpiresAt = now.AddDays(30 - index).Date,
                });
            }
        }

        var candidate = await database.Users.SingleOrDefaultAsync(
            user => user.Email == "john.doe@example.com", cancellationToken);
        if (candidate is null)
        {
            candidate = new User
            {
                Email = "john.doe@example.com",
                PasswordHash = passwordHasher.Hash("Candidate@123"),
                FirstName = "John",
                LastName = "Doe",
                CountryCode = "+966",
                Phone = "55 123 4567",
                Country = "Saudi Arabia",
                Nationality = "Saudi Arabia",
                Gender = "Male",
                City = "Riyadh",
                Title = "Full Stack Developer",
                About = "Passionate developer building reliable web applications.",
                Role = PortalRoles.Candidate,
                Preferences = new UserPreference(),
            };
            database.Users.Add(candidate);
        }

        var administrator = await database.Users.SingleOrDefaultAsync(
            user => user.Email == "admin@candidateportal.local", cancellationToken);
        if (administrator is null)
        {
            administrator = new User
            {
                Email = "admin@candidateportal.local",
                PasswordHash = passwordHasher.Hash("Admin@123"),
                FirstName = "Portal",
                LastName = "Administrator",
                CountryCode = "+966",
                Country = "Saudi Arabia",
                City = "Riyadh",
                Title = "PBICareerPosting Administrator",
                About = "PBICareerPosting administrator account.",
                Role = PortalRoles.Administrator,
                Preferences = new UserPreference(),
            };
            database.Users.Add(administrator);
        }

        var hrAdministrator = await database.Users.SingleOrDefaultAsync(
            user => user.Email == "hr.admin@candidateportal.local", cancellationToken);
        if (hrAdministrator is null)
        {
            hrAdministrator = new User
            {
                Email = "hr.admin@candidateportal.local",
                PasswordHash = passwordHasher.Hash("HrAdmin@1234"),
                FirstName = "HR",
                LastName = "Administrator",
                CountryCode = "+966",
                Country = "Saudi Arabia",
                City = "Riyadh",
                Title = "PBICareerPosting HR Administrator",
                About = "PBICareerPosting recruitment administration account.",
                Role = PortalRoles.HrAdministrator,
                Preferences = new UserPreference(),
            };
            database.Users.Add(hrAdministrator);
        }

        var student = await database.Users.SingleOrDefaultAsync(
            user => user.Email == "student@candidateportal.local", cancellationToken);
        if (student is null)
        {
            student = new User
            {
                Email = "student@candidateportal.local",
                PasswordHash = passwordHasher.Hash("Student@1234"),
                FirstName = "Portal",
                LastName = "Student",
                CountryCode = "+966",
                Country = "Saudi Arabia",
                City = "Riyadh",
                Title = "Student",
                About = "Cooperative training applicant.",
                Role = PortalRoles.Student,
                Preferences = new UserPreference(),
            };
            database.Users.Add(student);
        }
        await database.SaveChangesAsync(cancellationToken);

        foreach (var user in new[] { candidate, administrator, hrAdministrator, student })
        {
            if (!await database.UserPreferences.AnyAsync(
                    preference => preference.UserId == user.Id, cancellationToken))
            {
                database.UserPreferences.Add(new UserPreference { UserId = user.Id });
            }
        }

        if (!await database.Applications.AnyAsync(
                application => application.UserId == candidate.Id, cancellationToken))
        {
            var jobs = await database.Jobs.OrderBy(job => job.Id).Take(3).ToListAsync(cancellationToken);
            var statuses = new[] { "Under Review", "Interview", "Shortlisted" };
            for (var index = 0; index < jobs.Count; index++)
            {
                database.Applications.Add(new Application
                {
                    UserId = candidate.Id,
                    JobId = jobs[index].Id,
                    Status = statuses[index],
                    AppliedAt = jobs[index].PostedAt.AddHours(6),
                });
            }
        }

        if (!await database.Notifications.AnyAsync(
                notification => notification.UserId == candidate.Id, cancellationToken))
        {
            database.Notifications.AddRange(
                new Notification
                {
                    UserId = candidate.Id,
                    Kind = "application",
                    Title = "Interview stage update",
                    Message = "Your Business Analyst application has moved to the interview stage.",
                    Link = "/applications",
                    CreatedAt = now.AddHours(-2),
                },
                new Notification
                {
                    UserId = candidate.Id,
                    Kind = "application",
                    Title = "Application received",
                    Message = "We received your application for Senior React Developer.",
                    Link = "/applications",
                    CreatedAt = now.AddDays(-1),
                },
                new Notification
                {
                    UserId = candidate.Id,
                    Kind = "job",
                    Title = "New roles match your profile",
                    Message = "New engineering opportunities are available in Riyadh.",
                    Link = "/jobs",
                    CreatedAt = now.AddDays(-2),
                });
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
