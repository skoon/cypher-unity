<#
.SYNOPSIS
  Drives the Development player with real operating-system key presses and checks that the menus and the game flow answer them.

.DESCRIPTION
  The Editor tests have to send UI events straight to elements (UI Toolkit ignores input while the Editor is unfocused), so the real
  path -- OS key -> Input System -> InputForUI -> UI Toolkit navigation -> a MenuButton -- is only ever exercised here. The player is
  started with --default-settings, so nothing is written to the player's settings file. Keys are only sent while the player's window
  is the foreground window; if it cannot be brought to the front the script stops without sending anything.

  Needs a finished build (tools\unity-check.ps1 -Step Player). Screenshots of the window and the player log go to -OutDir.
#>
[CmdletBinding()]
param(
    [string]$Exe = '',
    [string]$OutDir = '',
    [int]$Width = 1280,
    [int]$Height = 720
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Exe) { $Exe = Join-Path $root '..\Builds\Dev\Cypher.exe' }
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP 'cypher-unity\keys' }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CypherWin
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
'@
[void][CypherWin]::SetProcessDPIAware()

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'player.log'
Remove-Item $log -ErrorAction SilentlyContinue
Get-ChildItem $OutDir -Filter '*.png' | Remove-Item

$vk = @{ Up = 0x26; Down = 0x28; Left = 0x25; Right = 0x27; Enter = 0x0D; Esc = 0x1B; PageUp = 0x21; PageDown = 0x22; J = 0x4A; W = 0x57 }
$extended = @('Up', 'Down', 'Left', 'Right', 'PageUp', 'PageDown')

$process = Start-Process $Exe -PassThru -ArgumentList @('-screen-width', $Width, '-screen-height', $Height, '-screen-fullscreen', 0, '-logFile', $log, '--default-settings', '--no-auto-pause')
$script:failures = New-Object System.Collections.Generic.List[string]

function Stop-Player { if ($process -and -not $process.HasExited) { $process.Kill() } }

function Get-Log { if (Test-Path $log) { Get-Content $log -ErrorAction SilentlyContinue } else { @() } }

function Wait-Log([string]$pattern, [int]$after, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $lines = @(Get-Log | Where-Object { $_ -match $pattern })
        if ($lines.Count -gt $after) { return $lines.Count }
        if ($process.HasExited) { throw "The player exited while waiting for: $what" }
        Start-Sleep -Milliseconds 200
    }
    throw "Waited $seconds s for: $what"
}

function Count-Log([string]$pattern) { @(Get-Log | Where-Object { $_ -match $pattern }).Count }

function Get-Window {
    $process.Refresh()
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The player has no window' }
    $process.MainWindowHandle
}

function Focus-Player {
    $h = Get-Window
    foreach ($try in 1..10) {
        if ([CypherWin]::GetForegroundWindow() -eq $h) { return }
        [void][CypherWin]::ShowWindow($h, 9)
        [void][CypherWin]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 300
    }
    if ([CypherWin]::GetForegroundWindow() -ne $h) { throw 'The player window could not be brought to the foreground, so no keys were sent.' }
}

function Send-Key([string]$name) {
    Focus-Player
    $code = [byte]$vk[$name]
    $scan = [byte][CypherWin]::MapVirtualKey($code, 0)
    $flags = if ($extended -contains $name) { 1 } else { 0 }
    [CypherWin]::keybd_event($code, $scan, $flags, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [CypherWin]::keybd_event($code, $scan, $flags -bor 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 450
}

function Take-Shot([string]$name) {
    Focus-Player
    $h = Get-Window
    $r = New-Object CypherWin+RECT
    [void][CypherWin]::GetClientRect($h, [ref]$r)
    $p = New-Object CypherWin+POINT
    [void][CypherWin]::ClientToScreen($h, [ref]$p)
    $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($p.X, $p.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# Press a key, then require the log to show the given line within a few seconds.
function Step([string]$label, [string]$key, [string]$pattern, [string]$shot) {
    $before = Count-Log $pattern
    Send-Key $key
    try {
        [void](Wait-Log $pattern $before 6 $label)
        Write-Host ("  ok    {0}" -f $label)
    }
    catch {
        Write-Host ("  FAIL  {0}" -f $label)
        $script:failures.Add($label)
    }
    if ($shot) { Take-Shot $shot }
}

try {
    Write-Host "== Real key presses against $Exe ($Width x $Height)"
    [void](Wait-Log 'flow Loading -> Title' 0 120 'the title screen')
    Start-Sleep -Seconds 3
    Focus-Player
    Take-Shot '01_title'
    Write-Host '  ok    the title came up'

    # Title: Down skips the greyed CONTINUE and lands on OPTIONS; Enter opens it.
    Send-Key 'Down'
    Take-Shot '02_title_down'
    Step 'Enter on OPTIONS opens the options' 'Enter' 'ui screen Options' '03_options'
    Send-Key 'Right'
    Take-Shot '04_quality_up'
    foreach ($i in 1..3) { Send-Key 'PageDown' }
    Take-Shot '05_controls_tab'
    Step 'Enter on a binding opens the key prompt' 'Enter' 'ui screen Rebind' '06_rebind_prompt'
    Step 'pressing J binds it and closes the prompt' 'J' 'ui screen Options' '07_after_rebind'
    Step 'Esc leaves the options for the title' 'Esc' 'ui screen Title' '08_back_on_title'

    # Back on the title the focus was remembered on OPTIONS: Up goes to NEW GAME.
    Send-Key 'Up'
    Step 'Enter on NEW GAME starts the game' 'Enter' 'flow Title -> Playing' $null
    Start-Sleep -Seconds 3
    Take-Shot '09_playing'
    Step 'Esc in the game pauses it' 'Esc' 'flow Playing -> Paused' '10_paused'
    Step 'Esc on the pause menu resumes' 'Esc' 'flow Paused -> Playing' $null
    Step 'Esc pauses again' 'Esc' 'flow Playing -> Paused' $null
    Send-Key 'Down'; Send-Key 'Down'
    Step 'Enter on RETURN TO TITLE asks first' 'Enter' 'ui screen ConfirmTitle' '11_confirm_title'
    Step 'Esc on the question says no' 'Esc' 'ui screen Pause' $null
}
catch {
    Write-Host "  ABORTED: $($_.Exception.Message)"
    $script:failures.Add($_.Exception.Message)
}
finally {
    Stop-Player
}

$errors = @(Get-Log | Where-Object { $_ -match 'Exception|NullReference|error CS' })
if ($errors.Count -gt 0) { Write-Host "  The player log has $($errors.Count) error line(s); first: $($errors[0])"; $script:failures.Add('errors in the player log') }
Get-Log | Where-Object { $_ -match '\[Cypher\] (flow|ui screen)' } | ForEach-Object { Write-Host "    $_" }

if ($script:failures.Count -gt 0) { Write-Host "FAILED ($($script:failures.Count)): $($script:failures -join '; ')"; exit 1 }
Write-Host "OK. Screenshots and log are in $OutDir"
