param(
    [string]$ProcessName = 'Orla',
    [ValidateRange(10, 3600)]
    [int]$DurationSeconds = 600,
    [ValidateRange(250, 10000)]
    [int]$IntervalMilliseconds = 1000,
    [ValidateRange(0, 300)]
    [int]$WarmupSeconds = 10,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$process = @(Get-Process -Name $ProcessName -ErrorAction Stop)
if ($process.Count -ne 1) {
    throw "Era esperado exatamente um processo $ProcessName; encontrados: $($process.Count)."
}
$process = $process[0]

if ($WarmupSeconds -gt 0) {
    Start-Sleep -Seconds $WarmupSeconds
}

$samples = New-Object Collections.Generic.List[object]
$logicalProcessors = [Environment]::ProcessorCount
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$previousAt = $stopwatch.Elapsed.TotalSeconds
$process.Refresh()
$previousCpu = $process.TotalProcessorTime.TotalSeconds

while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Milliseconds $IntervalMilliseconds
    $process.Refresh()
    if ($process.HasExited) {
        throw "$ProcessName encerrou durante a medição."
    }

    $now = $stopwatch.Elapsed.TotalSeconds
    $cpu = $process.TotalProcessorTime.TotalSeconds
    $elapsed = [Math]::Max(0.001, $now - $previousAt)
    $cpuPercent = (($cpu - $previousCpu) / $elapsed / $logicalProcessors) * 100
    $samples.Add([pscustomobject]@{
        AtSeconds = [Math]::Round($now, 3)
        CpuPercent = [Math]::Round([Math]::Max(0, $cpuPercent), 4)
        WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 3)
        PrivateMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 3)
        Handles = $process.HandleCount
        Threads = $process.Threads.Count
    })
    $previousAt = $now
    $previousCpu = $cpu
}

function Measure-Values([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    $average = ($ordered | Measure-Object -Average).Average
    $maximum = ($ordered | Measure-Object -Maximum).Maximum
    $p95Index = [Math]::Min($ordered.Count - 1, [Math]::Max(0, [Math]::Ceiling($ordered.Count * 0.95) - 1))
    [pscustomobject]@{
        Average = [Math]::Round($average, 3)
        P95 = [Math]::Round($ordered[$p95Index], 3)
        Maximum = [Math]::Round($maximum, 3)
    }
}

$result = [ordered]@{
    Process = $ProcessName
    Version = $process.FileVersion
    Path = $process.Path
    StartedAt = $process.StartTime.ToString('o')
    DurationSeconds = $DurationSeconds
    IntervalMilliseconds = $IntervalMilliseconds
    SampleCount = $samples.Count
    LogicalProcessors = $logicalProcessors
    CpuPercent = Measure-Values @($samples | ForEach-Object { $_.CpuPercent })
    WorkingSetMB = Measure-Values @($samples | ForEach-Object { $_.WorkingSetMB })
    PrivateMB = Measure-Values @($samples | ForEach-Object { $_.PrivateMB })
    Handles = Measure-Values @($samples | ForEach-Object { $_.Handles })
    Threads = Measure-Values @($samples | ForEach-Object { $_.Threads })
}

$json = $result | ConvertTo-Json -Depth 4
if ($OutputPath) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $resolvedOutput
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [IO.File]::WriteAllText($resolvedOutput, $json, [Text.Encoding]::UTF8)
}
$json
