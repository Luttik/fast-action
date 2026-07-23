#Requires -Version 5.1
<#
.SYNOPSIS
  Build and launch Fast Action (unpackaged WinUI tray app).

.EXAMPLE
  .\run.ps1
  .\run.ps1 -Release
  .\run.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [switch] $Release,
    [switch] $NoBuild,
    [ValidateSet("x64", "x86", "ARM64")]
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$configuration = if ($Release) { "Release" } else { "Debug" }
$project = Join-Path $root "src\FastAction\FastAction.csproj"

Get-Process -Name "FastAction" -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $NoBuild) {
    Write-Host "Building FastAction ($configuration | $Platform)..." -ForegroundColor Cyan
    dotnet build $project -c $configuration -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

$rid = "win-$($Platform.ToLowerInvariant())"
$tfm = "net10.0-windows10.0.26100.0"
$exe = Join-Path $root "src\FastAction\bin\$Platform\$configuration\$tfm\$rid\FastAction.exe"

if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe`nRun without -NoBuild first."
}

Write-Host "Starting Fast Action..." -ForegroundColor Green
Write-Host "  Hotkey: Win+Shift+Space" -ForegroundColor DarkGray
Write-Host "  Tray icon in the notification area" -ForegroundColor DarkGray
Write-Host "  Config: $env:LOCALAPPDATA\FastAction\config.yaml" -ForegroundColor DarkGray

Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
