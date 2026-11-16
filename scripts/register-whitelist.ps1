<#
.SYNOPSIS
    Registers TiaOpennessMcp.exe in Siemens TIA Portal Openness AllowList.
.DESCRIPTION
    Siemens TIA Portal Openness requires external applications to be whitelisted in
    the Windows Registry under HKLM:\SOFTWARE\Siemens\Automation\Openness\AllowList.
    This script computes the SHA-256 hash of TiaOpennessMcp.exe and registers it,
    preventing security popups and connection blocks.
#>

param(
    [string]$ExePath
)

if (-not $ExePath) {
    # Release layout: the exe next to this script. Repo layout: the Release build under src.
    $ExePath = @("$PSScriptRoot\TiaOpennessMcp.exe", "$PSScriptRoot\..\src\bin\Release\net48\TiaOpennessMcp.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $ExePath) { $ExePath = "$PSScriptRoot\..\src\bin\Release\net48\TiaOpennessMcp.exe" }
}
$resolvedPath = [System.IO.Path]::GetFullPath($ExePath)

if (-not (Test-Path $resolvedPath)) {
    Write-Error "Executable not found at: $resolvedPath"
    Write-Host "Please build the project first: dotnet build -c Release"
    exit 1
}

# Check for Administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Elevating privileges to register in HKLM registry..." -ForegroundColor Cyan
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ExePath `"$resolvedPath`""
    exit 0
}

try {
    $exeName = [System.IO.Path]::GetFileName($resolvedPath)
    $bytes = [System.IO.File]::ReadAllBytes($resolvedPath)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hash = [Convert]::ToBase64String($sha256.ComputeHash($bytes))
    # Same format TIA Portal writes when a user accepts its Openness prompt: last write time in UTC, invariant
    # culture. Local time or the current culture (e.g. Finnish "2026.10.26 21.21.30.848") gives an entry TIA ignores.
    $dateModified = (Get-Item $resolvedPath).LastWriteTimeUtc.ToString("yyyy/MM/dd HH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture)

    $regPath = "HKLM:\SOFTWARE\Siemens\Automation\Openness\AllowList\$exeName"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }

    # TIA keeps one subkey per accepted build (Entry, Entry (1), ...), possibly for other paths. Update the entry
    # for this path and drop its outdated duplicates; otherwise take the first free name instead of overwriting
    # another path's Entry.
    $mine = @(Get-ChildItem -LiteralPath $regPath | Where-Object { [string]$_.GetValue('Path') -eq $resolvedPath })
    if ($mine) {
        $entryPath = $mine[0].PSPath
        $mine | Select-Object -Skip 1 | ForEach-Object { Remove-Item -LiteralPath $_.PSPath -Recurse -Force }
    } else {
        $taken = @(Get-ChildItem -LiteralPath $regPath | ForEach-Object PSChildName)
        $name = 'Entry'
        for ($i = 1; $taken -contains $name; $i++) { $name = "Entry ($i)" }
        $entryPath = (New-Item -Path "$regPath\$name" -Force).PSPath
    }

    New-ItemProperty -LiteralPath $entryPath -Name "Path" -Value $resolvedPath -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $entryPath -Name "DateModified" -Value $dateModified -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $entryPath -Name "FileHash" -Value $hash -PropertyType String -Force | Out-Null

    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " SUCCESS: Registered in Siemens TIA Portal Openness AllowList!" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " Executable:   $resolvedPath"
    Write-Host " DateModified: $dateModified"
    Write-Host " FileHash:     $hash"
    Write-Host " Registry:     $(Convert-Path -LiteralPath $entryPath)"
    Write-Host ""
} catch {
    Write-Error "Failed to register in Openness AllowList: $_"
    exit 1
}
