param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Orla'
$installedExe = Join-Path $installDirectory 'Orla.exe'
$legacyDirectory = Join-Path $env:LOCALAPPDATA 'VictorShell'
$legacyExe = Join-Path $legacyDirectory 'VictorShell.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process Orla, VictorShell -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

foreach ($restorableExe in @($installedExe, $legacyExe)) {
    if (Test-Path -LiteralPath $restorableExe) {
        $restore = Start-Process -FilePath $restorableExe -ArgumentList '--restore' -PassThru -Wait
        if ($restore.ExitCode -ne 0) { throw "Falha ao restaurar a barra nativa: $($restore.ExitCode)" }
    }
}

Remove-ItemProperty -Path $runKey -Name 'Orla' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name 'VictorShell' -ErrorAction SilentlyContinue

if ($KeepSettings) {
    'Orla.exe', 'Orla.cs', 'README.md', 'LICENSE' | ForEach-Object {
        Remove-Item -LiteralPath (Join-Path $installDirectory $_) -Force -ErrorAction SilentlyContinue
    }
} elseif (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Output 'Orla removido e barra nativa restaurada.'
