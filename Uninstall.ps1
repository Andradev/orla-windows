param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'VictorShell'
$installedExe = Join-Path $installDirectory 'VictorShell.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process VictorShell -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

if (Test-Path -LiteralPath $installedExe) {
    $restore = Start-Process -FilePath $installedExe -ArgumentList '--restore' -PassThru -Wait
    if ($restore.ExitCode -ne 0) { throw "Falha ao restaurar a barra nativa: $($restore.ExitCode)" }
}

Remove-ItemProperty -Path $runKey -Name 'VictorShell' -ErrorAction SilentlyContinue

if ($KeepSettings) {
    'VictorShell.exe', 'VictorShell.cs', 'README.md', 'LICENSE' | ForEach-Object {
        Remove-Item -LiteralPath (Join-Path $installDirectory $_) -Force -ErrorAction SilentlyContinue
    }
} elseif (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Output 'VictorShell removido e barra nativa restaurada.'
