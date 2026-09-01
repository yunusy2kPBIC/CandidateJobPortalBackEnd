# PBICareerPosting .NET API

ASP.NET Core/EF Core port of the candidate portal backend. It uses SQL Server by default while preserving the bearer-token format, snake_case JSON contract, frontend routes, data model, and Microsoft Graph resources of the FastAPI application in `../backend`.

## Run locally

Prerequisites: .NET 10 SDK and SQL Server. The local configuration uses the `CandidatePortal` database on `DESKTOP-NCLK3BN` with Windows Authentication.

```powershell
cd backend-dotnet
Copy-Item .env.example .env
# Set SQLSERVER_CONNECTION_STRING and SECRET_KEY in .env.
dotnet restore
dotnet run --urls http://127.0.0.1:8000
```

The API reads `backend-dotnet/.env`, then process environment variables. SQL Server is selected with `DATABASE_PROVIDER=sqlserver`; `Integrated Security=True` uses the identity running the API. PostgreSQL remains available with `DATABASE_PROVIDER=postgresql` and `DATABASE_URL`.

To initialize and seed a new development database without starting the web host:

```powershell
$env:AUTO_CREATE_SCHEMA = 'true'
$env:SEED_DEMO_DATA = 'true'
dotnet run -- --seed-only
Remove-Item Env:AUTO_CREATE_SCHEMA,Env:SEED_DEMO_DATA
```

Keep automatic schema creation and demo seeding disabled during normal operation.

## Implemented areas

- Registration, login, database-backed sessions, logout, and password changes
- Candidate jobs, applications, profile, resume upload, dashboard, notifications, and preferences
- Administrator summaries, job CRUD, candidate/CV access, application review, and status updates
- SharePoint diagnostics, provisioning, portal synchronization, generic resource CRUD, recruitment requests, cooperative training documents, and resume libraries
- SQL Server Windows Authentication, optional PostgreSQL compatibility, and Microsoft Graph integration

Run verification with:

```powershell
dotnet format --no-restore --verify-no-changes
dotnet build --no-restore
```
