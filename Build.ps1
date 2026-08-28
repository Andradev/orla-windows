param(
    [string]$OutputDirectory = "$PSScriptRoot\dist"
)

$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$source = Join-Path $PSScriptRoot 'src\Orla.cs'
$icon = Join-Path $PSScriptRoot 'assets\orla.ico'
$output = Join-Path $OutputDirectory 'Orla.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Compilador .NET Framework 4.8 não encontrado.'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    "/out:`"$output`"",
    "/win32icon:`"$icon`"",
    "/reference:`"$(Join-Path $wpf 'PresentationCore.dll')`"",
    "/reference:`"$(Join-Path $wpf 'PresentationFramework.dll')`"",
    "/reference:`"$(Join-Path $wpf 'WindowsBase.dll')`"",
    "/reference:`"$(Join-Path $framework 'System.Xaml.dll')`"",
    "/reference:`"$(Join-Path $framework 'System.Windows.Forms.dll')`"",
    "/reference:`"$(Join-Path $framework 'System.Drawing.dll')`"",
    "/reference:`"$(Join-Path $framework 'System.dll')`"",
    "/reference:`"$(Join-Path $framework 'System.Core.dll')`"",
    "`"$source`""
)

$stdout = Join-Path $env:TEMP 'orla-csc.stdout.txt'
$stderr = Join-Path $env:TEMP 'orla-csc.stderr.txt'
Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
$process = Start-Process -FilePath $compiler -ArgumentList $arguments -Wait -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$compilerOutput = @(
    Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue
    Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue
) -join [Environment]::NewLine
if ($compilerOutput) {
    Write-Output $compilerOutput
}
if ($process.ExitCode -ne 0) {
    throw "Compilação falhou com código $($process.ExitCode)."
}

$item = Get-Item -LiteralPath $output
$hash = Get-FileHash -LiteralPath $output -Algorithm SHA256
[pscustomobject]@{
    Path = $item.FullName
    Version = $item.VersionInfo.FileVersion
    SizeBytes = $item.Length
    SHA256 = $hash.Hash
} | ConvertTo-Json
