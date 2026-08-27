param(
    [switch]$NoStartup,
    [switch]$DoNotStart
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Orla'
$installedExe = Join-Path $installDirectory 'Orla.exe'
$legacyDirectory = Join-Path $env:LOCALAPPDATA 'VictorShell'
$legacyExe = Join-Path $legacyDirectory 'VictorShell.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($env:OS -ne 'Windows_NT') {
    throw 'Orla só pode ser instalado no Windows.'
}

Get-Process Orla, VictorShell -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
foreach ($restorableExe in @($installedExe, $legacyExe)) {
    if (Test-Path -LiteralPath $restorableExe) {
        $restore = Start-Process -FilePath $restorableExe -ArgumentList '--restore' -PassThru -Wait
        if ($restore.ExitCode -ne 0) { throw "Falha ao restaurar a barra nativa: $($restore.ExitCode)" }
    }
}

& (Join-Path $PSScriptRoot 'Build.ps1')

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'settings.ini')) -and
    (Test-Path -LiteralPath (Join-Path $legacyDirectory 'settings.ini'))) {
    Copy-Item -LiteralPath (Join-Path $legacyDirectory 'settings.ini') -Destination (Join-Path $installDirectory 'settings.ini')
}
$settingsPath = Join-Path $installDirectory 'settings.ini'
if (Test-Path -LiteralPath $settingsPath) {
    $settingsLines = [IO.File]::ReadAllLines($settingsPath, [Text.Encoding]::UTF8)
    if ($settingsLines.Count -gt 0 -and $settingsLines[0] -like '# Victor Shell*') {
        $settingsLines[0] = '# Orla - configuração simples e reversível'
    }
    $settingsLines = @($settingsLines | Where-Object { $_ -notlike 'FluentSearchHotkey=*' })
    [IO.File]::WriteAllLines($settingsPath, $settingsLines, [Text.Encoding]::UTF8)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\Orla.exe') -Destination $installedExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Orla.cs') -Destination (Join-Path $installDirectory 'Orla.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $installDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $installDirectory 'LICENSE') -Force

Remove-ItemProperty -Path $runKey -Name 'VictorShell' -ErrorAction SilentlyContinue
if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name 'Orla' -ErrorAction SilentlyContinue
} else {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'Orla' -Type String -Value ('"' + $installedExe + '"')
}

if (-not $DoNotStart) {
    Start-Process -FilePath $installedExe
    Start-Sleep -Seconds 2
}

$hash = (Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash
Write-Output "Orla instalado em: $installedExe"
Write-Output "SHA256: $hash"
if (-not (Test-Path 'C:\Program Files\Fluent Search\FluentSearch.exe')) {
    Write-Warning 'Fluent Search não foi encontrado; a tecla Windows nativa será preservada.'
}
