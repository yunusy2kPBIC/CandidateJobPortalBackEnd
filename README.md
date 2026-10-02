# PBICareerPosting .NET API

ASP.NET Core/EF Core port of the candidate portal backend. It uses SQL Server by default while preserving the bearer-token format, snake_case JSON contract, frontend routes, data model, and Microsoft Graph resources of the FastAPI application in `../backend`.

## Run locally

Prerequisites: .NET 10 SDK and SQL Server. The local configuration uses the `CandidatePortal` database on `DESKTOP-NCLK3BN` with Windows Authentication.

```powershell
cd backend-dotnet
Copy-Item .env.example .env
# Set SQLSERVER_CONNECTION_STRING and SECRET_KEY in .env.
dotnet restore
dotnet tool restore
dotnet run
```

The API reads `backend-dotnet/.env`, then process environment variables. Configure SQL Server with `SQLSERVER_CONNECTION_STRING`; `Integrated Security=True` uses the identity running the API.

`EMAIL_ENABLED` is the single email-delivery switch. With `EMAIL_ENABLED=Y`, account-verification, password-recovery, and confirmation messages are sent through the configured Office 365 SMTP settings, and codes are never returned to the browser. With `EMAIL_ENABLED=N`, outgoing email is skipped and the generated code is returned for display on the local verification or reset page. Configure verification timing with `EMAIL_VERIFICATION_MINUTES` and `EMAIL_VERIFICATION_RESEND_SECONDS`.

Password-reset codes expire after `PASSWORD_RESET_MINUTES`, can be regenerated after `PASSWORD_RESET_RESEND_SECONDS`, and are limited by `PASSWORD_RESET_MAX_ATTEMPTS`. A successful reset consumes the code, revokes the user's active sessions, and sends a password-changed confirmation when email is enabled.

The public privacy notice is versioned with `PRIVACY_POLICY_VERSION`, `PRIVACY_EFFECTIVE_DATE`, and `PRIVACY_CONTACT_EMAIL`. Registration must submit the current version and acceptance flag. The API stores the accepted version, UTC timestamp, IP address, and user agent in `user_consents`; `GET /api/privacy/consent` returns the authenticated user's latest evidence.

Database schema changes use EF Core migrations. To initialize and seed a new development database without starting the web host:

```powershell
dotnet run -- --migrate
$env:SEED_DEMO_DATA = 'true'
dotnet run -- --seed-only
Remove-Item Env:SEED_DEMO_DATA
```

For a database created by an older version of the application, take a backup and establish the migration baseline once:

```powershell
dotnet run -- --baseline-existing-database
dotnet run -- --migrate
```

The baseline command applies only the legacy additive schema repairs required to reach the current model, verifies all mapped columns and indexes, and then records `InitialBaseline` in `__EFMigrationsHistory`. It refuses to baseline a new or incomplete database. The migrate command also refuses to run the initial create migration over an existing unbaselined portal database.

Normal API startup validates that no migrations are pending. Apply migrations as a separate deployment step before starting the new API version. Keep demo seeding disabled during normal operation. SharePoint schema provisioning is separate from SQL Server migrations and must be completed before deploying code that uses new SharePoint fields.

To create and review a future migration:

```powershell
dotnet ef migrations add DescriptiveMigrationName
dotnet ef migrations script --idempotent --output artifacts/database-migration.sql
```

## SharePoint schema migrations

SharePoint schema changes are versioned independently from SQL Server. Configure a separate SharePoint site for each environment, then run:

```powershell
dotnet run -- --migrate-sharepoint
```

The command creates the `Portal Schema Migrations` history list, applies or verifies all known additive schema migrations, and records the current version. It creates missing lists, libraries, columns, and choice values but does not delete SharePoint data. It also validates existing column types and resolves configured display names to the real SharePoint internal field names.

Run the SharePoint migration command after the SQL migration and before deploying a backend version that writes new SharePoint fields. Running it again is safe and repairs additive schema drift. The current SharePoint schema version is `SP202609300001_InitialBaseline`.

When demo seeding is enabled, the HR Administrator account is
`hr.admin@candidateportal.local` / `HrAdmin@1234`. For non-demo environments, set
`BOOTSTRAP_HR_ADMIN_EMAIL` and `BOOTSTRAP_HR_ADMIN_PASSWORD` together; the password
must contain at least 12 characters.

The seeded Student account is `student@candidateportal.local` / `Student@1234`.
Students can also create their own account by selecting Student during registration.

## Implemented areas

- Registration, password login, database-backed sessions, logout, and password changes
- Candidate jobs, applications, profile, resume upload, dashboard, notifications, and preferences
- Administrator and HR Administrator recruitment access, including job management, candidate/CV access, application review, and status updates
- Administrator-only audit logs, permanent job deletion, SharePoint diagnostics, provisioning, and portal synchronization
- SharePoint diagnostics, provisioning, portal synchronization, generic resource CRUD, recruitment requests, cooperative training documents, and resume libraries
- SQL Server Windows Authentication and Microsoft Graph integration

Run verification with:

```powershell
dotnet format --no-restore --verify-no-changes
dotnet build --no-restore
```
