param(
    [switch]$CreateSchema
)

$ErrorActionPreference = "Stop"

$projectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\CandidatePortal.Api.csproj"))
$previousSeedDemoData = [Environment]::GetEnvironmentVariable("SEED_DEMO_DATA", "Process")
$previousAutoCreateSchema = [Environment]::GetEnvironmentVariable("AUTO_CREATE_SCHEMA", "Process")

try {
    $env:SEED_DEMO_DATA = "true"
    if ($CreateSchema) {
        $env:AUTO_CREATE_SCHEMA = "true"
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

    if ($null -eq $previousAutoCreateSchema) {
        Remove-Item Env:AUTO_CREATE_SCHEMA -ErrorAction SilentlyContinue
    }
    else {
        $env:AUTO_CREATE_SCHEMA = $previousAutoCreateSchema
    }
}

Write-Host "Development seed data is ready." -ForegroundColor Green