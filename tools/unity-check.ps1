# Runs Unity headless against this project: build the Slice scene, run EditMode / PlayMode tests, capture screenshots.
#
#   powershell -File tools/unity-check.ps1 -Step Build
#   powershell -File tools/unity-check.ps1 -Step Edit
#   powershell -File tools/unity-check.ps1 -Step Play
#   powershell -File tools/unity-check.ps1 -Step Capture -CaptureDir D:\shots [-ScreenHeight 1080]
#   powershell -File tools/unity-check.ps1 -Step Player      (Build scene, then a Windows Development player in Builds\Dev)
#   powershell -File tools/unity-check.ps1 -Step Player -Release -PlayerDir D:\source\cypher-unity\Builds\Release   (the same without the Development flag: optimised scripts)
#   powershell -File tools/unity-check.ps1 -Step Benchmark   (run that player with --benchmark, print the results)
#   powershell -File tools/unity-check.ps1 -Step Perf        (Player, then Benchmark)
#   powershell -File tools/unity-check.ps1 -Step Shots       (run that player with --shots: a screenshot of every menu screen)
#   powershell -File tools/unity-check.ps1 -Step All     (Build, Edit, Play; stops at the first failure)
#
# Close the Editor first if it has this project open. Logs go to $env:TEMP\cypher-unity.
# Capture and PlayMode use the GPU (no -nographics); Build and EditMode run with -nographics.
# Edit and Play accept -TestFilter <semicolon-separated names or patterns> to run only some tests, e.g. -TestFilter Cypher.Tests.UiScreenTests
# Benchmark -Quality Performance|Low|Medium|High|Ultra pins that preset (adaptive quality is always off in a benchmark).
# Benchmark -NoCrowd leaves the streets empty: the control for what the people cost. -NoTraffic leaves the roads and the sky empty: the control for what the traffic costs; -NoVehicleLights keeps the traffic and takes the point lights off the vehicles. -NoAudio switches every sound off: the control for what the audio costs.
# Benchmark -Fire makes the pilot fire the gun in bursts (3 s of every 10 s) as it walks: Gate C's worst case.
# Benchmark -Route centre|city picks the loop (centre is the M1 loop round the plaza; city tours every edge: use -BenchmarkSeconds 240).
# Benchmark opens a borderless window of -Width x -Height for about warm-up + duration seconds; leave the machine alone meanwhile.
# Exit codes of the player: 0 passed the 30 FPS gate, 1 failed it, 2 an exception, 3 invalid run (the character did not walk).
param(
    [ValidateSet('Build', 'Edit', 'Play', 'Capture', 'Player', 'Benchmark', 'Perf', 'Shots', 'All')] [string]$Step = 'All',
    [string]$CaptureDir = '',
    [int]$ScreenHeight = 0,
    [int]$TimeoutSeconds = 420,
    [string]$PlayerDir = '',
    [string]$BenchmarkDir = '',
    [int]$Width = 1920,
    [int]$Height = 1080,
    [int]$BenchmarkSeconds = 60,
    [int]$WarmupSeconds = 10,
    [string]$Quality = '',
    [string]$Route = '',
    [switch]$NoCrowd,
    [switch]$NoTraffic,
    [switch]$NoVehicleLights,
    [switch]$NoAudio,
    [switch]$Fire,
    [switch]$Release,
    [string]$TestFilter = '',
    [string]$ShotsDir = '',
    [string]$Unity = 'D:\source\Unity\6000.6.6f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logs = Join-Path $env:TEMP 'cypher-unity'
New-Item -ItemType Directory -Force $logs | Out-Null
if (-not $PlayerDir) { $PlayerDir = Join-Path $project 'Builds\Dev' }
if (-not $BenchmarkDir) { $BenchmarkDir = Join-Path $logs 'benchmark' }
if (-not $ShotsDir) { $ShotsDir = Join-Path $logs 'shots' }

function Invoke-Unity([string[]]$UnityArgs, [string]$Log, [int]$Seconds = $TimeoutSeconds) {
    $p = Start-Process -FilePath $Unity -ArgumentList $UnityArgs -PassThru -NoNewWindow
    $null = $p.Handle   # without this, ExitCode reads as null after WaitForExit
    if (-not $p.WaitForExit($Seconds * 1000)) {
        # A hung editor would block forever, and its import workers keep the project locked: kill the whole tree.
        Write-Host "  TIMEOUT after $Seconds s: killing Unity (pid $($p.Id)) and its workers"
        & taskkill /PID $p.Id /T /F | Out-Null
        return 124
    }
    $errors = Select-String -Path $Log -Pattern 'error CS|Shader error|Scripts have compiler errors' -ErrorAction SilentlyContinue | Select-Object -First 10
    foreach ($e in $errors) { Write-Host "  $($e.Line)" }
    return $p.ExitCode
}

function Show-Results([string]$Xml) {
    if (-not (Test-Path $Xml)) { Write-Host '  no results file'; return $false }
    $run = ([xml](Get-Content $Xml)).'test-run'
    Write-Host "  $($run.result): $($run.passed)/$($run.total) passed, $($run.failed) failed"
    foreach ($t in $run.SelectNodes('//test-case[@result="Failed"]')) {
        Write-Host "  FAIL $($t.fullname)"
        Write-Host "       $($t.failure.message.'#cdata-section')"
    }
    return ([int]$run.failed -eq 0)
}

function Step-Build {
    Write-Host '== Build scene'
    $code = Invoke-Unity @('-batchmode', '-nographics', '-quit', '-projectPath', $project, '-executeMethod', 'Cypher.EditorTools.SliceSceneBuilder.Build', '-logFile', "$logs\build.log") "$logs\build.log"
    Write-Host "  exit $code"
    return ($code -eq 0)
}

function Step-Tests([string]$Mode) {
    Write-Host "== $Mode tests"
    $xml = "$logs\$Mode.xml"
    Remove-Item $xml -ErrorAction SilentlyContinue
    # PlayMode runs with a graphics device: reflection probes and other render tests skip without one.
    $a = @('-batchmode', '-projectPath', $project, '-runTests', '-testPlatform', $Mode, '-testResults', $xml, '-logFile', "$logs\$Mode.log")
    if ($Mode -eq 'EditMode') { $a += '-nographics' }
    if ($TestFilter) { $a += @('-testFilter', $TestFilter) }
    # the PlayMode suite is several minutes of scenes and sounds (the audio tests alone wait out seconds of real time each): give it room
    $code = Invoke-Unity $a "$logs\$Mode.log" ([Math]::Max($TimeoutSeconds, 1800))
    return ((Show-Results $xml) -and $code -eq 0)
}

function Step-Capture {
    Write-Host '== Capture'
    if (-not $CaptureDir) { throw 'Capture needs -CaptureDir' }
    $a = @('-batchmode', '-projectPath', $project, '-executeMethod', 'Cypher.EditorTools.SliceCapture.Run', '-captureDir', $CaptureDir, '-logFile', "$logs\capture.log")
    if ($ScreenHeight -gt 0) { $a += @('-captureScreenHeight', "$ScreenHeight") }
    $code = Invoke-Unity $a "$logs\capture.log"
    $n = if (Test-Path $CaptureDir) { (Get-ChildItem $CaptureDir -Filter *.png).Count } else { 0 }
    Write-Host "  exit $code, $n images in $CaptureDir"
    return ($code -eq 0 -and $n -gt 0)
}

function Step-Player {
    Write-Host '== Development player'
    if (-not (Step-Build)) { return $false }
    $exe = Join-Path $PlayerDir 'Cypher.exe'
    $buildArgs = @('-batchmode', '-quit', '-projectPath', $project, '-executeMethod', 'Cypher.EditorTools.PlayerBuilder.BuildFromCommandLine', '-buildOutput', $exe, '-logFile', "$logs\player-build.log")
    if ($Release) { $buildArgs += '-buildRelease' }
    $code = Invoke-Unity $buildArgs "$logs\player-build.log"
    Write-Host "  exit $code, $exe $(if (Test-Path $exe) { 'exists' } else { 'MISSING' })"
    return ($code -eq 0 -and (Test-Path $exe))
}

function Quote([string]$Text) { if ($Text -match '\s') { return '"' + $Text + '"' } return $Text }

function Step-Benchmark {
    Write-Host "== Benchmark ($Width x $Height, $WarmupSeconds s warm-up + $BenchmarkSeconds s$(if ($Quality) { ", $Quality" })$(if ($Route) { ", $Route route" })$(if ($Fire) { ", firing" }))"
    $exe = Join-Path $PlayerDir 'Cypher.exe'
    if (-not (Test-Path $exe)) { throw "No player at $exe; run -Step Player first" }
    New-Item -ItemType Directory -Force $BenchmarkDir | Out-Null
    $summary = Join-Path $BenchmarkDir 'benchmark_summary.txt'
    $started = Get-Date

    $a = @('-screen-width', $Width, '-screen-height', $Height, '-screen-fullscreen', 0, '-popupwindow',
        '--benchmark', '--benchmark-out', (Quote $BenchmarkDir), '--benchmark-seconds', $BenchmarkSeconds, '--benchmark-warmup', $WarmupSeconds,
        '-logFile', (Quote "$logs\player.log"))
    if ($Quality) { $a += @('--benchmark-quality', $Quality) }
    if ($Route) { $a += @('--benchmark-route', $Route) }
    if ($NoCrowd) { $a += '--no-crowd' }
    if ($NoTraffic) { $a += '--no-traffic' }
    if ($NoVehicleLights) { $a += '--no-vehicle-lights' }
    if ($NoAudio) { $a += '--no-audio' }
    if ($Fire) { $a += '--benchmark-fire' }
    $p = Start-Process -FilePath $exe -ArgumentList $a -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(($WarmupSeconds + $BenchmarkSeconds + 120) * 1000)) {
        Write-Host "  TIMEOUT: killing the player (pid $($p.Id))"
        & taskkill /PID $p.Id /T /F | Out-Null
        return $false
    }

    # A summary older than this run means the player died before writing one.
    if (-not (Test-Path $summary) -or (Get-Item $summary).LastWriteTime -lt $started) {
        Write-Host "  exit $($p.ExitCode) and no fresh summary; see $logs\player.log"
        return $false
    }
    Get-Content $summary | ForEach-Object { Write-Host "  $_" }
    $reason = switch ($p.ExitCode) { 0 { 'passed the 30 FPS gate' } 1 { 'FAILED the 30 FPS gate' } 2 { 'an exception was thrown' } 3 { 'invalid run: the character did not walk' } default { 'unexpected exit' } }
    Write-Host "  exit $($p.ExitCode): $reason. Results in $BenchmarkDir"
    return ($p.ExitCode -eq 0)
}

function Step-Shots {
    Write-Host "== Menu screenshots ($Width x $Height)"
    $exe = Join-Path $PlayerDir 'Cypher.exe'
    if (-not (Test-Path $exe)) { throw "No player at $exe; run -Step Player first" }
    New-Item -ItemType Directory -Force $ShotsDir | Out-Null
    $list = Join-Path $ShotsDir 'shots.txt'
    Remove-Item $list -ErrorAction SilentlyContinue   # the walk writes it last, so its presence means it finished

    $a = @('-screen-width', $Width, '-screen-height', $Height, '-screen-fullscreen', 0, '-popupwindow',
        '--shots', (Quote $ShotsDir), '-logFile', (Quote "$logs\shots.log"))
    $p = Start-Process -FilePath $exe -ArgumentList $a -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(240 * 1000)) {
        Write-Host "  TIMEOUT: killing the player (pid $($p.Id))"
        & taskkill /PID $p.Id /T /F | Out-Null
        return $false
    }
    if (-not (Test-Path $list)) { Write-Host "  exit $($p.ExitCode) and the walk did not finish; see $logs\shots.log"; return $false }
    Get-Content $list | ForEach-Object { Write-Host "  $_" }
    Write-Host "  exit $($p.ExitCode). Screenshots in $ShotsDir"
    return ($p.ExitCode -eq 0)
}

$ok = $true
switch ($Step) {
    'Build' { $ok = Step-Build }
    'Edit' { $ok = Step-Tests 'EditMode' }
    'Play' { $ok = Step-Tests 'PlayMode' }
    'Capture' { $ok = Step-Capture }
    'Player' { $ok = Step-Player }
    'Benchmark' { $ok = Step-Benchmark }
    'Shots' { $ok = Step-Shots }
    'Perf' { $ok = (Step-Player) -and (Step-Benchmark) }
    'All' { $ok = (Step-Build) -and (Step-Tests 'EditMode') -and (Step-Tests 'PlayMode') }
}
if ($ok) { Write-Host 'OK' } else { Write-Host 'FAILED'; exit 1 }
