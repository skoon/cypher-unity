# Runs Unity headless against this project: build the Slice scene, run EditMode / PlayMode tests, capture screenshots.
#
#   powershell -File tools/unity-check.ps1 -Step Build
#   powershell -File tools/unity-check.ps1 -Step Edit
#   powershell -File tools/unity-check.ps1 -Step Play
#   powershell -File tools/unity-check.ps1 -Step Capture -CaptureDir D:\shots [-ScreenHeight 1080]
#   powershell -File tools/unity-check.ps1 -Step All     (Build, Edit, Play; stops at the first failure)
#
# Close the Editor first if it has this project open. Logs go to $env:TEMP\cypher-unity.
# Capture and PlayMode use the GPU (no -nographics); Build and EditMode run with -nographics.
param(
    [ValidateSet('Build', 'Edit', 'Play', 'Capture', 'All')] [string]$Step = 'All',
    [string]$CaptureDir = '',
    [int]$ScreenHeight = 0,
    [int]$TimeoutSeconds = 420,
    [string]$Unity = 'D:\source\Unity\6000.6.6f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logs = Join-Path $env:TEMP 'cypher-unity'
New-Item -ItemType Directory -Force $logs | Out-Null

function Invoke-Unity([string[]]$UnityArgs, [string]$Log) {
    $p = Start-Process -FilePath $Unity -ArgumentList $UnityArgs -PassThru -NoNewWindow
    $null = $p.Handle   # without this, ExitCode reads as null after WaitForExit
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        # A hung editor would block forever, and its import workers keep the project locked: kill the whole tree.
        Write-Host "  TIMEOUT after $TimeoutSeconds s: killing Unity (pid $($p.Id)) and its workers"
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
    $code = Invoke-Unity $a "$logs\$Mode.log"
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

$ok = $true
switch ($Step) {
    'Build' { $ok = Step-Build }
    'Edit' { $ok = Step-Tests 'EditMode' }
    'Play' { $ok = Step-Tests 'PlayMode' }
    'Capture' { $ok = Step-Capture }
    'All' { $ok = (Step-Build) -and (Step-Tests 'EditMode') -and (Step-Tests 'PlayMode') }
}
if ($ok) { Write-Host 'OK' } else { Write-Host 'FAILED'; exit 1 }
