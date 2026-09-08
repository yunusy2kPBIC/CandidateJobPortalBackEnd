# PBICareerPosting .NET API

ASP.NET Core/EF Core port of the candidate portal backend. It uses SQL Server by default while preserving the bearer-token format, snake_case JSON contract, frontend routes, data model, and Microsoft Graph resources of the FastAPI application in `../backend`.

## Run locally

Prerequisites: .NET 10 SDK and SQL Server. The local configuration uses the `CandidatePortal` database on `DESKTOP-NCLK3BN` with Windows Authentication.

```powershell
cd backend-dotnet
Copy-Item .env.example .env
# Set SQLSERVER_CONNECTION_STRING and SECRET_KEY in .env.
dotnet restore
dotnet run
```

The API reads `backend-dotnet/.env`, then process environment variables. Configure SQL Server with `SQLSERVER_CONNECTION_STRING`; `Integrated Security=True` uses the identity running the API.

New account email verification defaults to development delivery. The API generates a six-digit code, logs a simulated message from `dev-no-reply@candidateportal.local`, and returns the code to the local verification screen. Configure the timing with `EMAIL_VERIFICATION_MINUTES` and `EMAIL_VERIFICATION_RESEND_SECONDS`; no mailbox or external email provider is needed in this mode.

To initialize and seed a new development database without starting the web host:

```powershell
$env:AUTO_CREATE_SCHEMA = 'true'
$env:SEED_DEMO_DATA = 'true'
dotnet run -- --seed-only
Remove-Item Env:AUTO_CREATE_SCHEMA,Env:SEED_DEMO_DATA
```

Keep automatic schema creation and demo seeding disabled during normal operation.

When demo seeding is enabled, the HR Administrator account is
`hr.admin@candidateportal.local` / `HrAdmin@1234`. For non-demo environments, set
`BOOTSTRAP_HR_ADMIN_EMAIL` and `BOOTSTRAP_HR_ADMIN_PASSWORD` together; the password
must contain at least 12 characters.

The seeded Student account is `student@candidateportal.local` / `Student@1234`.
Students can also create their own account by selecting Student during registration.

## Candidate sign-in with Google and Microsoft

Google and Microsoft candidate sign-in are optional. Password login remains available, and provider buttons appear only after the corresponding client ID and secret are configured in `.env`.

Create OAuth web applications with these local authorized redirect URIs:

- Google: `http://localhost:5173/signin-google`
- Microsoft: `http://localhost:5173/signin-microsoft`

Then configure:

```dotenv
GOOGLE_AUTH_CLIENT_ID=
GOOGLE_AUTH_CLIENT_SECRET=
MICROSOFT_AUTH_CLIENT_ID=
MICROSOFT_AUTH_CLIENT_SECRET=
```

The frontend must use an origin listed in `FRONTEND_ORIGIN`. Register HTTPS redirect URIs that use the deployed portal host for staging and production. External accounts are created with the candidate role only; administrator accounts cannot use the candidate social-login flow.

## Implemented areas

- Registration, password login, Google/Microsoft candidate login, database-backed sessions, logout, and password changes
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
