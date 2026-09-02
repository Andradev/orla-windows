param(
    [switch]$NoStartup,
    [switch]$DoNotStart,
    [switch]$SkipBuild,
    [string]$SourceExecutable
)

$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($env:OS -ne 'Windows_NT') {
    throw 'Orla can only be installed on Windows.'
}

if (-not $SkipBuild -and [string]::IsNullOrWhiteSpace($SourceExecutable)) {
    & (Join-Path $PSScriptRoot 'Build.ps1')
}

$sourceExe = if ([string]::IsNullOrWhiteSpace($SourceExecutable)) {
    Join-Path $PSScriptRoot 'dist\Orla.exe'
} else {
    [IO.Path]::GetFullPath($SourceExecutable)
}

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Source executable not found: $sourceExe"
}

$installer = Start-Process -FilePath $sourceExe -ArgumentList @('--install', '--silent') -PassThru -Wait
if ($installer.ExitCode -ne 0) {
    throw "Orla installer exited with code $($installer.ExitCode)."
}

$installedExe = Join-Path $env:LOCALAPPDATA 'Orla\Orla.exe'
if (-not (Test-Path -LiteralPath $installedExe)) {
    throw "The installed executable was not created: $installedExe"
}

if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name 'Orla' -ErrorAction SilentlyContinue
}

if ($DoNotStart) {
    Get-CimInstance Win32_Process -Filter "Name = 'Orla.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -eq $installedExe } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}

$hash = (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
Write-Output "Orla installed at: $installedExe"
Write-Output "SHA-256: $hash"
Write-Output "Starts with Windows: $(-not $NoStartup)"
