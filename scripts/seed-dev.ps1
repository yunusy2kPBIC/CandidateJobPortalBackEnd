param(
    [switch]$CreateSchema
)

$ErrorActionPreference = "Stop"

$projectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\CandidatePortal.Api.csproj"))
$previousSeedDemoData = [Environment]::GetEnvironmentVariable("SEED_DEMO_DATA", "Process")

try {
    $env:SEED_DEMO_DATA = "true"
    if ($CreateSchema) {
        dotnet run --project $projectPath -- --migrate
        if ($LASTEXITCODE -ne 0) {
            throw "Development database migration failed with exit code $LASTEXITCODE."
        }
    }

    dotnet run --project $projectPath -- --seed-only
    if ($LASTEXITCODE -ne 0) {
        throw "Development database seeding failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $previousSeedDemoData) {
        Remove-Item Env:SEED_DEMO_DATA -ErrorAction SilentlyContinue
    }
    else {
        $env:SEED_DEMO_DATA = $previousSeedDemoData
    }
}

Write-Host "Development seed data is ready." -ForegroundColor Green
