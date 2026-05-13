# DoorSim installer
#
# Installs DoorSim to C:\Genetec\DoorSim.
#
# This installer is intended for Genetec Security Center / Softwire lab servers.
# It checks required local services, enables Softwire simulation if needed, copies
# the DoorSim application files, and creates a desktop shortcut.

$ErrorActionPreference = "Stop"

$InstallRoot = "C:\Genetec"
$InstallPath = Join-Path $InstallRoot "DoorSim"

$SecurityCenterServiceName = "GenetecServer"
$SoftwireServiceName = "Genetec Softwire Controller Host"

$SoftwireH0Path = "C:\Program Files (x86)\Genetec\Softwire\H0"
$SoftwireFeaturesTargetPath = Join-Path $SoftwireH0Path "Features.XML"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppSourcePath = Join-Path $ScriptRoot "App"
$FeaturesSourcePath = Join-Path $ScriptRoot "Features.XML"

$ShortcutName = "DoorSim.lnk"
$ShortcutPath = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) $ShortcutName
$InstalledExePath = Join-Path $InstallPath "DoorSim.exe"
$InstalledIconPath = Join-Path $InstallPath "Images\AppIcon.ico"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host $Message -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "OK: $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "WARNING: $Message" -ForegroundColor Yellow
}

function Stop-Install {
    param([string]$Message)

    Write-Host ""
    Write-Host "INSTALLATION STOPPED" -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Press Enter to close this window..."
    Read-Host | Out-Null
    exit 1
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ServiceOrNull {
    param([string]$Name)

    return Get-Service -Name $Name -ErrorAction SilentlyContinue
}

Write-Host "DoorSim Installer" -ForegroundColor Cyan
Write-Host "=================" -ForegroundColor Cyan

Write-Step "Checking administrator permissions..."
if (-not (Test-IsAdministrator)) {
    Stop-Install "Please run this installer from an elevated PowerShell window using 'Run as Administrator'."
}
Write-Ok "Installer is running as Administrator."

Write-Step "Checking Security Center service..."
$securityCenterService = Get-ServiceOrNull $SecurityCenterServiceName
if ($null -eq $securityCenterService) {
    Stop-Install "Genetec Security Center service '$SecurityCenterServiceName' was not found. Install Security Center before installing DoorSim."
}
Write-Ok "Security Center service found: $SecurityCenterServiceName"

Write-Step "Checking local Security Center SQL service..."
$sqlServices = Get-Service | Where-Object {
    $_.Name -like "MSSQL*" -or
    $_.DisplayName -like "SQL Server (*"
}

if (-not $sqlServices -or $sqlServices.Count -eq 0) {
    Stop-Install "No local SQL Server service was found. DoorSim expects the Genetec Security Center Directory database to be on this server."
}
Write-Ok "Local SQL Server service detected."

Write-Step "Checking Softwire service..."
$softwireService = Get-ServiceOrNull $SoftwireServiceName
if ($null -eq $softwireService) {
    Stop-Install "Softwire service '$SoftwireServiceName' was not found. Install Softwire on this server before installing DoorSim."
}
Write-Ok "Softwire service found: $SoftwireServiceName"

Write-Step "Checking installer package files..."
if (-not (Test-Path $AppSourcePath)) {
    Stop-Install "The installer package is missing the App folder: $AppSourcePath"
}

if (-not (Test-Path (Join-Path $AppSourcePath "DoorSim.exe"))) {
    Stop-Install "The installer package App folder does not contain DoorSim.exe."
}

if (-not (Test-Path $FeaturesSourcePath)) {
    Stop-Install "The installer package is missing Features.XML."
}

Write-Ok "Installer package files found."

Write-Step "Creating install folder..."
if (-not (Test-Path $InstallRoot)) {
    New-Item -ItemType Directory -Path $InstallRoot | Out-Null
    Write-Ok "Created $InstallRoot"
}
else {
    Write-Ok "$InstallRoot already exists."
}

if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath | Out-Null
    Write-Ok "Created $InstallPath"
}
else {
    Write-Ok "$InstallPath already exists."
}

Write-Step "Copying DoorSim application files..."
Copy-Item -Path (Join-Path $AppSourcePath "*") -Destination $InstallPath -Recurse -Force
Write-Ok "DoorSim copied to $InstallPath"

Write-Step "Checking Softwire simulation enablement..."
if (-not (Test-Path $SoftwireH0Path)) {
    Stop-Install "Softwire H0 folder was not found: $SoftwireH0Path"
}

if (Test-Path $SoftwireFeaturesTargetPath) {
    Write-Ok "Softwire simulation appears to already be enabled. Features.XML exists."
}
else {
    Write-Warn "Features.XML was not found. Enabling Softwire simulation by copying Features.XML."

    Copy-Item -Path $FeaturesSourcePath -Destination $SoftwireFeaturesTargetPath -Force
    Write-Ok "Copied Features.XML to $SoftwireFeaturesTargetPath"

    Write-Step "Restarting Softwire service..."
    Restart-Service -Name $SoftwireServiceName -Force
    Write-Ok "Softwire service restarted."
}

Write-Step "Creating desktop shortcut..."
if (-not (Test-Path $InstalledExePath)) {
    Stop-Install "DoorSim.exe was not found after copy: $InstalledExePath"
}

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = $InstalledExePath
$shortcut.WorkingDirectory = $InstallPath
$shortcut.Description = "DoorSim - Softwire Door Simulation & Testing Tool"

if (Test-Path $InstalledIconPath) {
    $shortcut.IconLocation = $InstalledIconPath
}

$shortcut.Save()

Write-Ok "Desktop shortcut created: $ShortcutPath"

Write-Host ""
Write-Host "DoorSim installation completed successfully." -ForegroundColor Green
Write-Host ""
Write-Host "Installed to: $InstallPath"
Write-Host "Shortcut:     $ShortcutPath"
Write-Host ""
Write-Host "Press Enter to close this window..."
Read-Host | Out-Null