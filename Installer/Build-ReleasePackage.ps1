# Builds the DoorSim release package.
#
# This script publishes DoorSim as a self-contained Windows x64 application.
# Self-contained means the .NET runtime is bundled with the published app, so the
# target machine does not need .NET 8 installed separately.

$ErrorActionPreference = "Stop"

# Resolve paths relative to this script.
#
# Expected repository structure:
#   RepoRoot\
#       DoorSim\
#           DoorSim.csproj
#       Installer\
#           Build-ReleasePackage.ps1
#           Install-DoorSim.ps1
#           Install-DoorSim.bat
#           Features.XML
#
# $PSScriptRoot = RepoRoot\Installer
# RepoRoot       = parent folder of Installer
$InstallerSource = $PSScriptRoot
$RepoRoot = Split-Path -Parent $InstallerSource

$ProjectPath = Join-Path $RepoRoot "DoorSim\DoorSim.csproj"
$ReleaseRoot = Join-Path $RepoRoot "Release"
$PublishOutput = Join-Path $ReleaseRoot "publish"
$PackageRoot = Join-Path $ReleaseRoot "DoorSim_Installer"
$PackageAppFolder = Join-Path $PackageRoot "App"

Write-Host "Building DoorSim release package..." -ForegroundColor Cyan

# Clean previous release output.
if (Test-Path $ReleaseRoot) {
    Write-Host "Removing previous release folder..."
    Remove-Item $ReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
New-Item -ItemType Directory -Path $PackageRoot | Out-Null
New-Item -ItemType Directory -Path $PackageAppFolder | Out-Null

# Publish DoorSim as self-contained.
Write-Host "Publishing DoorSim self-contained for win-x64..."

dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:EnableCompressionInSingleFile=false `
    -o $PublishOutput

# Copy published app files into package App folder.
Write-Host "Copying published app files..."
Copy-Item -Path (Join-Path $PublishOutput "*") -Destination $PackageAppFolder -Recurse -Force

# Copy installer resources.
Write-Host "Copying installer resources..."

Copy-Item -Path (Join-Path $InstallerSource "Features.XML") `
    -Destination (Join-Path $PackageRoot "Features.XML") `
    -Force

# Install-DoorSim.ps1 will be created in the next step.
if (Test-Path (Join-Path $InstallerSource "Install-DoorSim.ps1")) {
    Copy-Item -Path (Join-Path $InstallerSource "Install-DoorSim.ps1") `
        -Destination (Join-Path $PackageRoot "Install-DoorSim.ps1") `
        -Force
}

if (Test-Path (Join-Path $InstallerSource "Install-DoorSim.bat")) {
    Copy-Item -Path (Join-Path $InstallerSource "Install-DoorSim.bat") `
        -Destination (Join-Path $PackageRoot "Install-DoorSim.bat") `
        -Force
}

Write-Host ""
Write-Host "Release package created at:" -ForegroundColor Green
Write-Host $PackageRoot -ForegroundColor Green
Write-Host ""
Write-Host "Next step: create/run Install-DoorSim.ps1." -ForegroundColor Yellow