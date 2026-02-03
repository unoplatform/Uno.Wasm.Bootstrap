# Validates that uno-config.js references a dotnet.js fingerprint that matches an actual file
param(
    [Parameter(Mandatory=$true)]
    [string]$PublishPath
)

$ErrorActionPreference = "Stop"

Write-Host "Validating dotnet.js fingerprint in: $PublishPath"

# Find uno-config.js (it could be in a package_* subdirectory)
$unoConfig = Get-ChildItem -Path $PublishPath -Recurse -Filter "uno-config.js" | Select-Object -First 1

if (-not $unoConfig) {
    Write-Host "ERROR: uno-config.js not found in $PublishPath"
    exit 1
}

Write-Host "Found uno-config.js: $($unoConfig.FullName)"

# Read the content and extract the fingerprint
$content = Get-Content -Path $unoConfig.FullName -Raw
$match = [regex]::Match($content, 'dotnet\.([a-z0-9]+)\.js')

if (-not $match.Success) {
    Write-Host "ERROR: Could not extract dotnet.js fingerprint from uno-config.js"
    Write-Host "Content of uno-config.js:"
    Write-Host $content
    exit 1
}

$fingerprint = $match.Groups[1].Value
Write-Host "Fingerprint in uno-config.js: $fingerprint"

# Check if the fingerprinted dotnet.js file exists
$frameworkPath = Join-Path $PublishPath "_framework"
$dotnetJsFile = Join-Path $frameworkPath "dotnet.$fingerprint.js"

if (-not (Test-Path $dotnetJsFile)) {
    Write-Host "ERROR: Fingerprint mismatch!"
    Write-Host "  uno-config.js references: dotnet.$fingerprint.js"
    Write-Host "  But this file does not exist in: $frameworkPath"
    Write-Host ""
    Write-Host "Available dotnet.*.js files in _framework:"
    Get-ChildItem -Path $frameworkPath -Filter "dotnet.*.js" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.Name)" }
    exit 1
}

Write-Host "SUCCESS: dotnet.$fingerprint.js exists in _framework"
Write-Host "Fingerprint validation passed!"
