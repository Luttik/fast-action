# Sync Lucide icons into the app.
# Downloads the official lucide-icons release zip and normalizes stroke color
# for WinUI SvgImageSource (currentColor often does not render).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sync_lucide_icons.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sync_lucide_icons.ps1 -Version 1.25.0

param(
    [string]$Version = "1.25.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $repoRoot "src\FastAction\Assets\Icons\lucide"
$tmp = Join-Path $env:TEMP "lucide-icons-$([guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Force -Path $dest | Out-Null
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    $zip = Join-Path $tmp "lucide-icons.zip"
    $url = "https://github.com/lucide-icons/lucide/releases/download/$Version/lucide-icons-$Version.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $tmp -Force

    $sample = Get-ChildItem -Path $tmp -Recurse -Filter "*.svg" | Select-Object -First 1
    if ($null -eq $sample) {
        throw "No SVG files found in release archive."
    }

    $svgRoot = $sample.Directory.FullName
    $svgs = Get-ChildItem -Path $svgRoot -Filter "*.svg"
    Write-Host "Found $($svgs.Count) SVGs in $svgRoot"

    Get-ChildItem -Path $dest -Filter "*.svg" -ErrorAction SilentlyContinue | Remove-Item -Force

    foreach ($svg in $svgs) {
        $content = Get-Content -Path $svg.FullName -Raw
        $content = $content -replace 'stroke="currentColor"', 'stroke="#888888"'
        $content = $content -replace "stroke='currentColor'", "stroke='#888888'"
        Set-Content -Path (Join-Path $dest $svg.Name) -Value $content -Encoding utf8 -NoNewline
    }

    Write-Host "Wrote $($svgs.Count) icons to $dest"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
