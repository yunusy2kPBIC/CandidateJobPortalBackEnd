using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<ExternalAuthCode> ExternalAuthCodes => Set<ExternalAuthCode>();
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string timestampType = "datetime2";

        var user = modelBuilder.Entity<User>();
        user.ToTable("users").HasKey(x => x.Id);
        user.Property(x => x.Id).HasColumnName("id");
        user.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
        user.HasIndex(x => x.Email).IsUnique();
        user.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
        user.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100);
        user.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100);
        user.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(32);
        user.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40);
        user.Property(x => x.Country).HasColumnName("country").HasMaxLength(100);
        user.Property(x => x.Nationality).HasColumnName("nationality").HasMaxLength(50);
        user.Property(x => x.Gender).HasColumnName("gender").HasMaxLength(20);
        user.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
        user.Property(x => x.Title).HasColumnName("title").HasMaxLength(150);
        user.Property(x => x.About).HasColumnName("about");
        user.Property(x => x.Role).HasColumnName("role").HasMaxLength(30);
        user.HasIndex(x => x.Role);
        user.Property(x => x.IsEmailVerified).HasColumnName("is_email_verified");
        user.Property(x => x.ResumeName).HasColumnName("resume_name").HasMaxLength(255);
        user.Property(x => x.ResumePath).HasColumnName("resume_path").HasMaxLength(500);
        user.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);

        var job = modelBuilder.Entity<Job>();
        job.ToTable("jobs").HasKey(x => x.Id);
        job.Property(x => x.Id).HasColumnName("id");
        job.Property(x => x.Title).HasColumnName("title").HasMaxLength(180);
        job.Property(x => x.Division).HasColumnName("division").HasMaxLength(120);
        job.Property(x => x.Country).HasColumnName("country").HasMaxLength(100);
        job.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
        job.Property(x => x.JobFunction).HasColumnName("job_function").HasMaxLength(120);
        job.Property(x => x.CareerLevel).HasColumnName("career_level").HasMaxLength(80);
        job.Property(x => x.EmploymentType).HasColumnName("employment_type").HasMaxLength(80);
        job.Property(x => x.Summary).HasColumnName("summary");
        job.Property(x => x.Description).HasColumnName("description");
        job.Property(x => x.Requirements).HasColumnName("requirements");
        job.Property(x => x.IsOpen).HasColumnName("is_open");
        job.Property(x => x.IsFeatured).HasColumnName("is_featured");
        job.Property(x => x.PostedAt).HasColumnName("posted_at").HasColumnType(timestampType);
        job.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType(timestampType);
        job.HasIndex(x => x.Title); job.HasIndex(x => x.Division); job.HasIndex(x => x.Country);
        job.HasIndex(x => x.City); job.HasIndex(x => x.JobFunction); job.HasIndex(x => x.CareerLevel);

        var application = modelBuilder.Entity<Application>();
        application.ToTable("applications").HasKey(x => x.Id);
        application.Property(x => x.Id).HasColumnName("id");
        application.Property(x => x.UserId).HasColumnName("user_id");
        application.Property(x => x.JobId).HasColumnName("job_id");
        application.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
        application.Property(x => x.AppliedAt).HasColumnName("applied_at").HasColumnType(timestampType);
        application.HasIndex(x => new { x.UserId, x.JobId }).IsUnique().HasDatabaseName("uq_user_job");
        application.HasOne(x => x.User).WithMany(x => x.Applications).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        application.HasOne(x => x.Job).WithMany(x => x.Applications).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);

        var session = modelBuilder.Entity<AuthSession>();
        session.ToTable("auth_sessions").HasKey(x => x.Id);
        session.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
        session.Property(x => x.UserId).HasColumnName("user_id");
        session.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        session.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType(timestampType);
        session.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType(timestampType);
        session.HasOne(x => x.User).WithMany(x => x.AuthSessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var externalLogin = modelBuilder.Entity<ExternalLogin>();
        externalLogin.ToTable("external_logins").HasKey(x => x.Id);
        externalLogin.Property(x => x.Id).HasColumnName("id");
        externalLogin.Property(x => x.UserId).HasColumnName("user_id");
        externalLogin.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30);
        externalLogin.Property(x => x.ProviderUserId).HasColumnName("provider_user_id").HasMaxLength(255);
        externalLogin.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
        externalLogin.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        externalLogin.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique().HasDatabaseName("uq_external_login");
        externalLogin.HasOne(x => x.User).WithMany(x => x.ExternalLogins).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var externalAuthCode = modelBuilder.Entity<ExternalAuthCode>();
        externalAuthCode.ToTable("external_auth_codes").HasKey(x => x.CodeHash);
        externalAuthCode.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        externalAuthCode.Property(x => x.UserId).HasColumnName("user_id");
        externalAuthCode.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        externalAuthCode.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType(timestampType);
        externalAuthCode.HasIndex(x => x.ExpiresAt);
        externalAuthCode.HasOne(x => x.User).WithMany(x => x.ExternalAuthCodes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var emailVerification = modelBuilder.Entity<EmailVerification>();
        emailVerification.ToTable("email_verifications").HasKey(x => x.UserId);
        emailVerification.Property(x => x.UserId).HasColumnName("user_id");
        emailVerification.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        emailVerification.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        emailVerification.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        emailVerification.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType(timestampType);
        emailVerification.Property(x => x.ResendAvailableAt).HasColumnName("resend_available_at").HasColumnType(timestampType);
        emailVerification.Property(x => x.ConsumedAt).HasColumnName("consumed_at").HasColumnType(timestampType);
        emailVerification.HasIndex(x => x.ExpiresAt);
        emailVerification.HasOne(x => x.User).WithOne(x => x.EmailVerification)
            .HasForeignKey<EmailVerification>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var passwordReset = modelBuilder.Entity<PasswordReset>();
        passwordReset.ToTable("password_resets").HasKey(x => x.UserId);
        passwordReset.Property(x => x.UserId).HasColumnName("user_id");
        passwordReset.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        passwordReset.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        passwordReset.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        passwordReset.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType(timestampType);
        passwordReset.Property(x => x.ResendAvailableAt).HasColumnName("resend_available_at").HasColumnType(timestampType);
        passwordReset.Property(x => x.ConsumedAt).HasColumnName("consumed_at").HasColumnType(timestampType);
        passwordReset.HasIndex(x => x.ExpiresAt);
        passwordReset.HasOne(x => x.User).WithOne(x => x.PasswordReset)
            .HasForeignKey<PasswordReset>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var userConsent = modelBuilder.Entity<UserConsent>();
        userConsent.ToTable("user_consents").HasKey(x => x.Id);
        userConsent.Property(x => x.Id).HasColumnName("id");
        userConsent.Property(x => x.UserId).HasColumnName("user_id");
        userConsent.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(50);
        userConsent.Property(x => x.DocumentVersion).HasColumnName("document_version").HasMaxLength(50);
        userConsent.Property(x => x.AcceptedAt).HasColumnName("accepted_at").HasColumnType(timestampType);
        userConsent.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
        userConsent.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        userConsent.HasIndex(x => new { x.UserId, x.DocumentType, x.DocumentVersion }).IsUnique()
            .HasDatabaseName("uq_user_consent_document_version");
        userConsent.HasOne(x => x.User).WithMany(x => x.Consents)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var notification = modelBuilder.Entity<Notification>();
        notification.ToTable("notifications").HasKey(x => x.Id);
        notification.Property(x => x.Id).HasColumnName("id");
        notification.Property(x => x.UserId).HasColumnName("user_id");
        notification.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(50);
        notification.Property(x => x.Title).HasColumnName("title").HasMaxLength(180);
        notification.Property(x => x.Message).HasColumnName("message");
        notification.Property(x => x.Link).HasColumnName("link").HasMaxLength(500);
        notification.Property(x => x.IsRead).HasColumnName("is_read");
        notification.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        notification.HasOne(x => x.User).WithMany(x => x.Notifications).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var preference = modelBuilder.Entity<UserPreference>();
        preference.ToTable("user_preferences").HasKey(x => x.UserId);
        preference.Property(x => x.UserId).HasColumnName("user_id");
        preference.Property(x => x.EmailUpdates).HasColumnName("email_updates");
        preference.Property(x => x.JobAlerts).HasColumnName("job_alerts");
        preference.Property(x => x.Marketing).HasColumnName("marketing");
        preference.Property(x => x.Language).HasColumnName("language").HasMaxLength(20);
        preference.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(20);
        preference.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType(timestampType);
        preference.HasOne(x => x.User).WithOne(x => x.Preferences).HasForeignKey<UserPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var auditLog = modelBuilder.Entity<AuditLog>();
        auditLog.ToTable("audit_logs").HasKey(x => x.Id);
        auditLog.Property(x => x.Id).HasColumnName("id");
        auditLog.Property(x => x.AdminUserId).HasColumnName("admin_user_id");
        auditLog.Property(x => x.Action).HasColumnName("action").HasMaxLength(50);
        auditLog.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(80);
        auditLog.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(100);
        auditLog.Property(x => x.Details).HasColumnName("details");
        auditLog.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType(timestampType);
        auditLog.HasIndex(x => x.CreatedAt);
        auditLog.HasOne(x => x.AdminUser).WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);

        var lookup = modelBuilder.Entity<LookupValue>();
        lookup.ToTable("lookup_values").HasKey(x => x.Id);
        lookup.Property(x => x.Id).HasColumnName("id");
        lookup.Property(x => x.Category).HasColumnName("category").HasMaxLength(50);
        lookup.Property(x => x.Value).HasColumnName("value").HasMaxLength(150);
        lookup.Property(x => x.ParentValue).HasColumnName("parent_value").HasMaxLength(150);
        lookup.Property(x => x.SortOrder).HasColumnName("sort_order");
        lookup.Property(x => x.IsActive).HasColumnName("is_active");
        lookup.HasIndex(x => new { x.Category, x.Value, x.ParentValue }).IsUnique().HasDatabaseName("uq_lookup_value");
        lookup.HasIndex(x => new { x.Category, x.IsActive, x.SortOrder }).HasDatabaseName("ix_lookup_category_active_order");
    }
}
