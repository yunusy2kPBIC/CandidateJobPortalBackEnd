using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Data;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var timestampType = Database.IsSqlServer() ? "datetime2" : "timestamp without time zone";

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
        user.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
        user.Property(x => x.Title).HasColumnName("title").HasMaxLength(150);
        user.Property(x => x.About).HasColumnName("about");
        user.Property(x => x.Role).HasColumnName("role").HasMaxLength(30);
        user.HasIndex(x => x.Role);
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
    }
}
