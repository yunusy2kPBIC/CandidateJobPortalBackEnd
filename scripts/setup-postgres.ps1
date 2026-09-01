param(
    [string]$PostgresHost = "localhost",
    [int]$Port = 5432,
    [string]$AdminUser = "postgres",
    [string]$ApplicationUser = "candidate_portal",
    [string]$DatabaseName = "candidate_portal"
)

$ErrorActionPreference = "Stop"

foreach ($identifier in @($ApplicationUser, $DatabaseName)) {
    if ($identifier -notmatch '^[a-zA-Z][a-zA-Z0-9_]{0,62}$') {
        throw "Database and role names must begin with a letter and contain only letters, numbers, or underscores."
    }
}

$psqlPath = (Get-Command psql.exe -ErrorAction SilentlyContinue).Source
if (-not $psqlPath) {
    $psqlPath = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $psqlPath) {
    throw "PostgreSQL psql.exe was not found. Install PostgreSQL before running this script."
}

$adminCredential = Get-Credential -UserName $AdminUser -Message "Enter the PostgreSQL administrator password"
$applicationCredential = Get-Credential -UserName $ApplicationUser -Message "Choose a password for the Candidate Portal database role"
$adminPassword = $adminCredential.GetNetworkCredential().Password
$applicationPassword = $applicationCredential.GetNetworkCredential().Password

if ([string]::IsNullOrWhiteSpace($applicationPassword)) {
    throw "The application database password cannot be empty."
}

$sqlScript = Join-Path $PSScriptRoot "setup_database.sql"
$psqlArguments = @(
    "-w",
    "--host=$PostgresHost",
    "--port=$Port",
    "--username=$AdminUser",
    "--dbname=postgres",
    "--set=ON_ERROR_STOP=1",
    "--set=app_user=$ApplicationUser",
    "--set=app_password=$applicationPassword",
    "--set=app_database=$DatabaseName",
    "--file=$sqlScript"
)

try {
    $env:PGPASSWORD = $adminPassword
    & $psqlPath @psqlArguments
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL database setup failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    $adminPassword = $null
}

$randomBytes = New-Object byte[] 32
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$randomGenerator.GetBytes($randomBytes)
$randomGenerator.Dispose()
$secretKey = ([BitConverter]::ToString($randomBytes)).Replace("-", "").ToLowerInvariant()
$encodedPassword = [Uri]::EscapeDataString($applicationPassword)
$environmentPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.env"))

$environmentContent = @"
DATABASE_URL=postgresql+psycopg://${ApplicationUser}:${encodedPassword}@${PostgresHost}:${Port}/${DatabaseName}
SECRET_KEY=$secretKey
ACCESS_TOKEN_MINUTES=480
FRONTEND_ORIGIN=http://localhost:5174
STORAGE_PROVIDER=local
LOCAL_STORAGE_PATH=../storage
AUTO_CREATE_SCHEMA=false
SEED_DEMO_DATA=true
BOOTSTRAP_ADMIN_EMAIL=
BOOTSTRAP_ADMIN_PASSWORD=
"@
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($environmentPath, $environmentContent, $utf8WithoutBom)

$applicationPassword = $null
$pythonPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.venv\Scripts\python.exe"))
if (-not (Test-Path -LiteralPath $pythonPath)) {
    throw "Backend virtual environment not found. Create it and install requirements before running this script."
}

Push-Location ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..")))
try {
    & $pythonPath -m alembic upgrade head
    if ($LASTEXITCODE -ne 0) {
        throw "Alembic migration failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "Candidate Portal PostgreSQL database is ready." -ForegroundColor Green
Write-Host "Configuration written to $environmentPath"
