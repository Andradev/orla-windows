param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$installedExe = Join-Path $env:LOCALAPPDATA 'Orla\Orla.exe'

if (Test-Path -LiteralPath $installedExe) {
    $arguments = @('--uninstall', '--silent')
    if ($KeepSettings) { $arguments += '--keep-settings' }
    $uninstaller = Start-Process -FilePath $installedExe -ArgumentList $arguments -PassThru -Wait
    if ($uninstaller.ExitCode -ne 0) {
        throw "Orla uninstaller exited with code $($uninstaller.ExitCode)."
    }
    Start-Sleep -Seconds 2
} else {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Orla' -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'Orla\taskbar-state.txt') -Force -ErrorAction SilentlyContinue
    Write-Warning 'The Orla executable was not found. Stale startup and taskbar-state entries were removed.'
}

Write-Output 'Orla was removed and the original Windows taskbar state was restored.'
if ($KeepSettings) {
    Write-Output "Settings were kept at: $(Join-Path $env:LOCALAPPDATA 'Orla')"
}
