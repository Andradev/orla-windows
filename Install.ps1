param(
    [switch]$NoStartup,
    [switch]$DoNotStart
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'VictorShell'
$installedExe = Join-Path $installDirectory 'VictorShell.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($env:OS -ne 'Windows_NT') {
    throw 'VictorShell só pode ser instalado no Windows.'
}

Get-Process VictorShell -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
if (Test-Path -LiteralPath $installedExe) {
    $restore = Start-Process -FilePath $installedExe -ArgumentList '--restore' -PassThru -Wait
    if ($restore.ExitCode -ne 0) { throw "Falha ao restaurar a barra nativa: $($restore.ExitCode)" }
}

& (Join-Path $PSScriptRoot 'Build.ps1')

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\VictorShell.exe') -Destination $installedExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\VictorShell.cs') -Destination (Join-Path $installDirectory 'VictorShell.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $installDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $installDirectory 'LICENSE') -Force

if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name 'VictorShell' -ErrorAction SilentlyContinue
} else {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'VictorShell' -Type String -Value ('"' + $installedExe + '"')
}

if (-not $DoNotStart) {
    Start-Process -FilePath $installedExe
    Start-Sleep -Seconds 2
}

$hash = (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
Write-Output "VictorShell instalado em: $installedExe"
Write-Output "SHA256: $hash"
if (-not (Test-Path 'C:\Program Files\Fluent Search\FluentSearch.exe')) {
    Write-Warning 'Fluent Search não foi encontrado; a tecla Windows nativa será preservada.'
}
