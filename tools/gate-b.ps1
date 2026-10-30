# Gate B: the default tier, fully populated, p99 frame time at most 33.3 ms. Runs the Development player's benchmark on the Street route for every tier, several times, alternating the
# tiers round the repeats so that drift in the machine spreads over all of them, and prints one table.
#
#   powershell -File tools/gate-b.ps1                          (Street route, five tiers, three runs each, 160 s recorded: about an hour; takes over the screen)
#   powershell -File tools/gate-b.ps1 -Route junction -Repeats 1 -Seconds 90
#   powershell -File tools/gate-b.ps1 -Tiers Medium,High -Repeats 2
#   powershell -File tools/gate-b.ps1 -Tiers Medium,Ultra -Fire          (Gate C: the same with the pilot firing the gun in bursts)
#   powershell -File tools/gate-b.ps1 -ReportFrom D:\somewhere\gate-b-xyz      (just print the table for runs already made)
#
# Before each run it waits until the machine is quiet (CPU under -QuietPercent for -QuietSeconds in a row) and gives up after -QuietTimeoutSeconds rather than measure a busy machine.
# It refuses to start when the player is older than the newest source file (the player must be rebuilt with unity-check.ps1 -Step Player first: a benchmark runs the player as it is).
# Each run's summary, hitch lists and player log are kept in -OutDir\<tier>-<n>\. Leave the machine alone while it runs.
param(
    [ValidateSet('centre', 'city', 'street', 'junction')] [string]$Route = 'street',
    [string[]]$Tiers = @('Performance', 'Low', 'Medium', 'High', 'Ultra'),
    [int]$Repeats = 3,
    [int]$Seconds = 0,
    [int]$WarmupSeconds = 10,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [double]$QuietPercent = 15,
    [int]$QuietSeconds = 10,
    [int]$QuietTimeoutSeconds = 300,
    [double]$GateMs = 33.33,
    [double]$Headroom = 0.2,
    [string]$PlayerDir = '',
    [string]$OutDir = '',
    [string]$ReportFrom = '',
    [switch]$NoAudio,
    [switch]$Fire,
    [switch]$AllowStalePlayer
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# powershell -File hands 'Medium,Ultra' over as one string
$Tiers = @($Tiers | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($t in $Tiers) { if ($t -notin 'Performance', 'Low', 'Medium', 'High', 'Ultra') { throw "Unknown tier '$t'" } }
if (-not $PlayerDir) { $PlayerDir = Join-Path $project 'Builds\Dev' }
$logs = Join-Path $env:TEMP 'cypher-unity'

function Read-Summary([string]$Dir) {
    $file = Join-Path $Dir 'benchmark_summary.txt'
    if (-not (Test-Path $file)) { return $null }
    $h = @{}
    foreach ($line in Get-Content $file) {
        $i = $line.IndexOf(': ')
        if ($i -gt 0) { $h[$line.Substring(0, $i)] = $line.Substring($i + 2) }
    }
    return $h
}

function Num($h, [string]$key) {
    if (-not $h.ContainsKey($key)) { return $null }
    $d = 0.0
    if ([double]::TryParse($h[$key], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$d)) { return $d }
    return $null
}

function Median([double[]]$Values) {
    $s = $Values | Sort-Object
    $n = $s.Count
    if ($n -eq 0) { return $null }
    if ($n % 2 -eq 1) { return $s[($n - 1) / 2] }
    return ($s[$n / 2 - 1] + $s[$n / 2]) / 2
}

function Write-Table([string]$Root) {
    $rows = @()
    foreach ($dir in Get-ChildItem $Root -Directory | Sort-Object Name) {
        $h = Read-Summary $dir.FullName
        if ($null -eq $h) { continue }
        $tier = ($h['quality'] -split ',')[0]
        $rows += [pscustomobject]@{
            Run = $dir.Name; Tier = $tier; Result = $h['result']; Fps = Num $h 'avg_fps'; P99 = Num $h 'p99_ms'; Worst = Num $h 'worst_ms'
            Over = Num $h 'frames_over_budget'; Gpu = Num $h 'gpu_avg_ms'; Cpu = Num $h 'cpu_avg_ms'; Draws = Num $h 'draw_calls_avg'
            People = Num $h 'people_drawn_avg'; Cars = Num $h 'vehicles_drawn_avg'; Metres = Num $h 'distance_m'; Held = Num $h 'stalled_s'
            AllocKb = Num $h 'alloc_kb_avg'; Gc = Num $h 'gc_frames'; AudioShare = Num $h 'audio_thread_share_avg'; Launch = Num $h 'launch_to_play_s'
        }
    }
    if ($rows.Count -eq 0) { Write-Host "No runs in $Root"; return }
    $rows | Format-Table Run, Tier, Result, Fps, P99, Worst, Over, Gpu, Cpu, Draws, People, Cars, Metres, Held, AllocKb, Gc, Launch -AutoSize | Out-String -Width 220 | Write-Host

    $limit = $GateMs * (1 - $Headroom)
    $summary = foreach ($g in $rows | Group-Object Tier) {
        $p99 = [double[]]($g.Group | ForEach-Object { $_.P99 })
        $valid = @($g.Group | Where-Object { $_.Result -ne 'INVALID' }).Count
        [pscustomobject]@{
            Tier = $g.Name; Runs = $g.Count; Invalid = $g.Count - $valid
            MedianP99 = [math]::Round((Median $p99), 2); WorstP99 = [math]::Round(($p99 | Measure-Object -Maximum).Maximum, 2)
            Passes = if (@($g.Group | Where-Object { $_.Result -eq 'PASS' }).Count -eq $g.Count) { 'yes' } else { 'NO' }
            WithHeadroom = if (($p99 | Measure-Object -Maximum).Maximum -le $limit -and $valid -eq $g.Count) { 'yes' } else { 'no' }
            MedianFps = [math]::Round((Median ([double[]]($g.Group | ForEach-Object { $_.Fps }))), 1)
        }
    }
    $order = @('Performance', 'Low', 'Medium', 'High', 'Ultra')
    $summary = $summary | Sort-Object { $order.IndexOf($_.Tier) }
    Write-Host ("Per tier ('Passes': every run's p99 at most {0} ms; 'WithHeadroom': every run's p99 at most {1:0.0} ms, {2:P0} under the gate)" -f $GateMs, $limit, $Headroom)
    $summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $best = $summary | Where-Object { $_.WithHeadroom -eq 'yes' } | Select-Object -Last 1
    if ($best) { Write-Host "Highest tier with headroom: $($best.Tier)" } else { Write-Host 'No tier has headroom.' }
}

if ($ReportFrom) {
    Write-Table $ReportFrom
    return
}

# the player must be as new as the code
$exe = Join-Path $PlayerDir 'Cypher.exe'
if (-not (Test-Path $exe)) { throw "No player at $exe; run unity-check.ps1 -Step Player first" }
# an incremental build leaves the exe alone and rewrites what is under Cypher_Data (the assemblies, the scene)
$built = (Get-ChildItem (Join-Path $PlayerDir 'Cypher_Data') -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
$newest = Get-ChildItem (Join-Path $project 'Assets\_Cypher'), (Join-Path $project 'Assets\Scenes') -Recurse -File -Include *.cs, *.shader, *.hlsl, *.cginc, *.unity, *.mixer, *.asset |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newest.LastWriteTime -gt $built -and -not $AllowStalePlayer) {
    throw "The player ($built) is older than $($newest.FullName) ($($newest.LastWriteTime)): run unity-check.ps1 -Step Player first (or pass -AllowStalePlayer)"
}

if (-not $OutDir) { $OutDir = Join-Path $logs ($(if ($Fire) { 'gate-c-' } else { 'gate-b-' }) + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $OutDir | Out-Null
if ($Seconds -le 0) { $Seconds = switch ($Route) { 'street' { 160 } 'junction' { 90 } 'city' { 240 } default { 60 } } }
Write-Host "Gate $(if ($Fire) { 'C' } else { 'B' }): $Route route$(if ($Fire) { ', firing' }), $($Tiers -join ', '), $Repeats runs each, $WarmupSeconds s warm-up + $Seconds s recorded; results in $OutDir"

function Wait-Quiet {
    $deadline = (Get-Date).AddSeconds($QuietTimeoutSeconds)
    $quiet = 0
    $last = 0
    while ($quiet -lt $QuietSeconds) {
        if ((Get-Date) -gt $deadline) { throw "The machine never went quiet (CPU $([math]::Round($last))% for the last second; wanted under $QuietPercent% for $QuietSeconds s)" }
        $last = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 1).CounterSamples[0].CookedValue
        if ($last -lt $QuietPercent) { $quiet++ } else { $quiet = 0 }
    }
}

$check = Join-Path $PSScriptRoot 'unity-check.ps1'
for ($r = 1; $r -le $Repeats; $r++) {
    foreach ($tier in $Tiers) {
        Wait-Quiet
        $cpu = [math]::Round((Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 1).CounterSamples[0].CookedValue)
        $dir = Join-Path $OutDir "$tier-$r"
        Write-Host "== $tier, run $r of $Repeats (CPU $cpu% before)"
        $a = @('-File', $check, '-Step', 'Benchmark', '-Route', $Route, '-Quality', $tier, '-BenchmarkSeconds', $Seconds, '-WarmupSeconds', $WarmupSeconds,
            '-Width', $Width, '-Height', $Height, '-PlayerDir', $PlayerDir, '-BenchmarkDir', $dir)
        if ($NoAudio) { $a += '-NoAudio' }
        if ($Fire) { $a += '-Fire' }
        & powershell @a | Out-Null
        $log = Join-Path $logs 'player.log'
        if (Test-Path $log) { Copy-Item $log (Join-Path $dir 'player.log') -Force }
        $h = Read-Summary $dir
        if ($null -eq $h) { Write-Host "   no summary: the player died; see $dir\player.log"; continue }
        Write-Host ("   {0}: {1} FPS, p99 {2} ms, worst {3} ms, {4} frames over, held up {5} s" -f $h['result'], $h['avg_fps'], $h['p99_ms'], $h['worst_ms'], $h['frames_over_budget'], $h['stalled_s'])
    }
}

Write-Table $OutDir *>&1 | Tee-Object -FilePath (Join-Path $OutDir 'gate-b-table.txt')
