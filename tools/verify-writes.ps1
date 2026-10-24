# verify-writes.ps1 - guided hardware test of every setting CompxTray can write.
# It changes settings temporarily, asks you to check the effect on the mouse,
# and puts your own settings back at the end (also if the test is aborted).
# Run:  tools\verify-writes.bat   (results in tools\verify-report.txt)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$src = Join-Path $root 'src\Device'
$out = Join-Path $here 'verify-report.txt'
Add-Type -Path @((Join-Path $src 'CompxDevice.cs'), (Join-Path $src 'ConfigMap.cs'), (Join-Path $src 'MouseConfig.cs'))

$report = New-Object 'System.Collections.Generic.List[string]'
function Log([string]$s) { $report.Add($s) }
function Say([string]$s, [string]$color = 'Gray') { Write-Host $s -ForegroundColor $color; Log $s }
function Result([string]$name, [string]$status, [string]$detail) {
    $c = 'Yellow'; if ($status -eq 'PASS') { $c = 'Green' } elseif ($status -eq 'FAIL') { $c = 'Red' }
    $line = '{0,-5} {1}: {2}' -f $status, $name, $detail
    Write-Host $line -ForegroundColor $c
    Log ('RESULT ' + $line)
}
function Ask([string]$q) {
    while ($true) {
        $a = (Read-Host "$q [y/n/u = unsure]").Trim().ToLower()
        if ($a -eq 'y' -or $a -eq 'n' -or $a -eq 'u') { Log "QUESTION: $q -> $a"; return $a }
    }
}
function Wait([string]$msg) { Read-Host "$msg  (then press Enter)" | Out-Null; Log "STEP: $msg" }

function WithDev([scriptblock]$body) {
    $err = $null
    $d = [CompxTray.Device.CompxDevice]::Open([ref]$err)
    if ($d -eq $null) { throw "Receiver not found: $err" }
    try { return (& $body $d) }
    finally { foreach ($l in $d.Log) { Log "      $l" }; $d.Dispose() }
}
function ReadCfg { WithDev { param($d) [CompxTray.Device.MouseConfig]::Read($d) } }
function ActiveStage { WithDev { param($d) $r = $d.ReadEeprom(0, 6); if ($r -eq $null) { -1 } else { [int]$r[4] } } }
function ApplyCfg($old, $new) {
    WithDev { param($d)
        $lines = [CompxTray.Device.MouseConfig]::Apply($d, $old, $new)
        foreach ($l in $lines) { Log "   write: $l" }
        $fresh = [CompxTray.Device.MouseConfig]::Read($d)
        $failed = @($lines | Where-Object { $_.StartsWith('FAILED') }).Count
        New-Object PSObject -Property @{ Config = $fresh; Failed = $failed; Lines = ($lines -join '; ') }
    }
}
function Describe($c) {
    $dpi = ($c.Dpi[0..($c.StageCount - 1)]) -join '/'
    '{0} Hz, {1} stages (stored active index {2}), DPI {3}, lift-off {4}, debounce {5} ms, motion sync {6}, sleep {7} s' -f `
        $c.PollingHz, $c.StageCount, $c.ActiveStage, $dpi, $c.LiftOffCode, $c.DebounceMs, $c.MotionSync, $c.SleepSeconds
}

# ---------------------------------------------------------------------------------------------
$tray = @(Get-Process CompxTray -ErrorAction SilentlyContinue)
$trayPath = $null
if ($tray.Count -gt 0) {
    $trayPath = $tray[0].Path
    Write-Host "Closing the running CompxTray during the test (it is restarted at the end)."
    $tray | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Say "=== CompxTray write verification $(Get-Date -Format s) ===" 'Cyan'
Say "Each step changes a setting, checks it by reading it back, and asks you what you notice."
Say "Your own settings are restored at the end."
Wait "Move the mouse so that it is awake"

$orig = ReadCfg
$ids = WithDev { param($d) '{0} {1:X4}:{2:X4} fw {3}' -f $d.ProductName, $d.Vid, $d.Pid, $d.ReadFirmware() }
Say "Device: $ids"
$verified = WithDev { param($d) [CompxTray.Device.MouseModel]::Find($d.Vid, $d.Pid, $d.ProductName).SettingsVerified }
if (-not $verified) {
    Say "This model is not on the verified list. Run tools\read-settings.ps1 first and check that the decoded values" 'Yellow'
    Say "match what you know about the mouse. The test writes to the mouse and restores it afterwards." 'Yellow'
    if ((Read-Host "Type YES to continue") -ne 'YES') { Say 'Stopped.'; $report | Out-File -FilePath $out -Encoding UTF8; if ($trayPath) { Start-Process $trayPath }; exit 1 }
}
Say ("Start:  " + (Describe $orig))
$orig.ToBackupText("verify-writes start") | Out-File (Join-Path $here 'verify-start-backup.txt') -Encoding ASCII
$cur = $orig
$done = $false

try {
    # 0. Keep the mouse awake during the test
    $n = $cur.Clone(); $n.SleepSeconds = 600
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0 -and $cur.SleepSeconds -eq 600) { Result 'Sleep timer write' 'PASS' 'set to 10 min for the duration of the test' }
    else { Result 'Sleep timer write' 'FAIL' $r.Lines }

    # 1. How the active stage is stored
    Say "`n--- 1. Active DPI stage ---" 'Cyan'
    $count = $cur.StageCount
    $seq = @(ActiveStage)
    for ($i = 1; $i -le $count; $i++) { Wait "Press the DPI button on the mouse ONCE ($i of $count)"; $seq += (ActiveStage) }
    Say ("Stored active index after each press: " + ($seq -join ' -> '))
    $uniq = @($seq | Sort-Object -Unique)
    $min = ($uniq | Measure-Object -Minimum).Minimum; $max = ($uniq | Measure-Object -Maximum).Maximum
    $activeStored = $uniq.Count -gt 1
    if (-not $activeStored) { Result 'Active stage' 'INFO' "the mouse does not store the active stage when the button is pressed (stays $($uniq[0]))" }
    elseif ($min -eq 0 -and $max -eq $count - 1) { Result 'Active stage' 'PASS' 'stored zero-based (0..count-1), as CompxTray assumes' }
    elseif ($min -eq 1 -and $max -eq $count) { Result 'Active stage' 'FAIL' 'stored one-based (1..count); CompxTray shows the wrong stage' }
    else { Result 'Active stage' 'INFO' "unexpected values $($uniq -join ',')" }
    if ($seq[0] -ne $seq[-1]) { Say "Note: the mouse did not end on the stage it started on." 'Yellow' }

    # 2. DPI values take effect
    Say "`n--- 2. DPI values ---" 'Cyan'
    $n = $cur.Clone(); for ($i = 0; $i -lt $n.StageCount; $i++) { $n.Dpi[$i] = 200 }
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0) { Result 'DPI write (200)' 'PASS' 'all stages read back as 200' } else { Result 'DPI write (200)' 'FAIL' $r.Lines }
    $slow = Ask "Move the mouse. Is the pointer now MUCH SLOWER than normal?"
    if ($slow -ne 'y') {
        Wait "Press the DPI button $count times (a full round) and move the mouse again"
        $slow2 = Ask "Is the pointer MUCH SLOWER now?"
        if ($slow2 -eq 'y') { Result 'DPI effect' 'INFO' 'new DPI applies only after switching stage' } else { Result 'DPI effect (slow)' 'FAIL' 'no visible change' }
    } else { Result 'DPI effect (slow)' 'PASS' 'pointer slowed down immediately' }

    $n = $cur.Clone(); for ($i = 0; $i -lt $n.StageCount; $i++) { $n.Dpi[$i] = 6400 }
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0) { Result 'DPI write (6400)' 'PASS' 'all stages read back as 6400' } else { Result 'DPI write (6400)' 'FAIL' $r.Lines }
    $fast = Ask "Move the mouse. Is the pointer now MUCH FASTER than normal?"
    if ($fast -eq 'y') { Result 'DPI effect (fast)' 'PASS' 'pointer sped up' } else { Result 'DPI effect (fast)' 'FAIL' "answer: $fast" }

    $n = $cur.Clone(); for ($i = 0; $i -lt 8; $i++) { $n.Dpi[$i] = $orig.Dpi[$i] }
    $r = ApplyCfg $cur $n; $cur = $r.Config
    Say "DPI values restored: $(($cur.Dpi[0..($cur.StageCount - 1)]) -join '/')"

    # 3. Number of stages
    Say "`n--- 3. Number of DPI stages ---" 'Cyan'
    if ($cur.StageCount -lt 6) {
        $extra = $cur.StageCount
        $n = $cur.Clone(); $n.StageCount = $cur.StageCount + 1; $n.Dpi[$extra] = 6400
        $r = ApplyCfg $cur $n; $cur = $r.Config
        if ($r.Failed -eq 0 -and $cur.StageCount -eq $n.StageCount) { Result 'Stage count write' 'PASS' "now $($cur.StageCount) stages" } else { Result 'Stage count write' 'FAIL' $r.Lines }
        Say "Stage $($extra + 1) was added at 6400 DPI (very fast)."
        $seq = @(ActiveStage)
        for ($i = 1; $i -le $cur.StageCount; $i++) { Wait "Press the DPI button ONCE ($i of $($cur.StageCount)) and move the mouse a little"; $seq += (ActiveStage) }
        Say ("Stored active index after each press: " + ($seq -join ' -> '))
        $maxSeen = ($seq | Measure-Object -Maximum).Maximum
        if ($activeStored -and $maxSeen -eq $cur.StageCount - 1) { Result 'Stage count effect' 'PASS' "the mouse cycled through the added stage (index $maxSeen)" }
        else {
            $reached = Ask "Was one of the stages very fast (the new 6400 DPI stage)?"
            if ($reached -eq 'y') { Result 'Stage count effect' 'PASS' 'you noticed the added stage' } else { Result 'Stage count effect' 'FAIL' "answer: $reached" }
        }
        $n = $cur.Clone(); $n.StageCount = $orig.StageCount; $n.Dpi[$extra] = $orig.Dpi[$extra]
        $r = ApplyCfg $cur $n; $cur = $r.Config
        Say "Stage count restored to $($cur.StageCount)."
    } else { Result 'Stage count' 'INFO' 'skipped, already 6 stages' }

    # 4. Lift-off distance
    Say "`n--- 4. Lift-off distance ---" 'Cyan'
    Say "Tip: a stack of about 10 sheets of paper is 1 mm, 20 sheets 2 mm. Or just lift slowly and watch the pointer."
    $n = $cur.Clone(); $n.LiftOffCode = 2
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0 -and $cur.LiftOffCode -eq 2) { Result 'Lift-off write (High)' 'PASS' 'reads back as 2' } else { Result 'Lift-off write (High)' 'FAIL' $r.Lines }
    Wait "HIGH: move the mouse slowly sideways while lifting it straight up. Notice when the pointer stops. Put it down"
    $n = $cur.Clone(); $n.LiftOffCode = 1
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0 -and $cur.LiftOffCode -eq 1) { Result 'Lift-off write (Low)' 'PASS' 'reads back as 1' } else { Result 'Lift-off write (Low)' 'FAIL' $r.Lines }
    Wait "LOW: do the same again"
    $lod = Ask "Did the pointer stop at a clearly LOWER height with LOW than with HIGH?"
    if ($lod -eq 'y') { Result 'Lift-off effect' 'PASS' 'Low (1) stops tracking lower than High (2)' }
    elseif ($lod -eq 'n') { Result 'Lift-off effect' 'FAIL' 'no noticeable difference between code 1 and 2' }
    else { Result 'Lift-off effect' 'INFO' 'unsure' }
    $hiMm = Read-Host "Optional: roughly how high could you lift with HIGH before it stopped, in mm (Enter to skip)"
    if ($hiMm) { Log "High lift-off estimate: $hiMm mm" }
    $n = $cur.Clone(); $n.LiftOffCode = $orig.LiftOffCode
    $r = ApplyCfg $cur $n; $cur = $r.Config

    # 5. Debounce and 6. motion sync (written and read back; no visible effect to check)
    Say "`n--- 5-6. Debounce and motion sync ---" 'Cyan'
    $n = $cur.Clone(); $n.DebounceMs = 2; $n.MotionSync = -not $cur.MotionSync
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -eq 0 -and $cur.DebounceMs -eq 2) { Result 'Debounce write' 'PASS' 'reads back as 2 ms' } else { Result 'Debounce write' 'FAIL' $r.Lines }
    if ($r.Failed -eq 0 -and $cur.MotionSync -eq $n.MotionSync) { Result 'Motion sync write' 'PASS' "reads back as $($cur.MotionSync)" } else { Result 'Motion sync write' 'FAIL' $r.Lines }
    $clicks = Ask "Click normally a few times. Do single clicks still work as single clicks (no accidental double clicks)?"
    if ($clicks -eq 'y') { Result 'Debounce 2 ms' 'PASS' 'clicks behave normally' } else { Result 'Debounce 2 ms' 'INFO' "answer: $clicks" }

    # 7. Settings survive power off
    Say "`n--- 7. Settings survive switching the mouse off ---" 'Cyan'
    $n = $cur.Clone(); $n.PollingHz = 500; $n.DebounceMs = 4; $n.Dpi[0] = 450; $n.LiftOffCode = 2
    $r = ApplyCfg $cur $n; $cur = $r.Config
    if ($r.Failed -ne 0) { Result 'Persistence setup' 'FAIL' $r.Lines }
    $expect = $cur
    Wait "Switch the mouse OFF with the switch underneath, wait 5 seconds, switch it ON and move it"
    $after = ReadCfg
    Say ("After power cycle: " + (Describe $after))
    if ($after.SameSettings($expect)) { Result 'Persistence' 'PASS' 'all settings kept after power off' } else { Result 'Persistence' 'FAIL' ('expected ' + (Describe $expect)) }
    $cur = $after

    # 8. Restore from the app's original backup
    Say "`n--- 8. Restore original from backup ---" 'Cyan'
    $tag = WithDev { param($d) '{0:X4}-{1:X4}' -f $d.Vid, $d.Pid }
    $bk = Join-Path $env:APPDATA "CompxTray\backup-$tag.txt"
    if (Test-Path $bk) {
        $text = [IO.File]::ReadAllText($bk)
        $res = WithDev { param($d)
            $lines = [CompxTray.Device.MouseConfig]::Restore($d, $text)
            New-Object PSObject -Property @{ Lines = ($lines -join '; '); Failed = @($lines | Where-Object { $_.StartsWith('FAILED') }).Count; Config = [CompxTray.Device.MouseConfig]::Read($d) }
        }
        $cur = $res.Config
        if ($res.Failed -eq 0) { Result 'Restore from backup' 'PASS' ('now ' + (Describe $cur)) } else { Result 'Restore from backup' 'FAIL' $res.Lines }
    } else { Result 'Restore from backup' 'INFO' "no backup file at $bk" }

    # 9. Put the user's own settings back
    Say "`n--- 9. Putting your settings back ---" 'Cyan'
    $r = ApplyCfg $cur $orig; $cur = $r.Config
    if ($cur.SameSettings($orig)) { Result 'Final restore' 'PASS' (Describe $cur); $done = $true }
    else { Result 'Final restore' 'FAIL' ('now ' + (Describe $cur)) }
}
catch {
    Say ("ERROR: " + $_.Exception.Message) 'Red'
    Log $_.ScriptStackTrace
}
finally {
    if (-not $done) {
        try {
            Say "Restoring your settings..." 'Yellow'
            $now = ReadCfg
            $r = ApplyCfg $now $orig
            if ($r.Config.SameSettings($orig)) { Say "Your settings are back." 'Green' }
            else { Say "Could not fully restore. Use 'Restore original' in CompxTray or tools\verify-start-backup.txt." 'Red' }
        } catch { Say ("Restore failed: " + $_.Exception.Message + ". Move the mouse and run the test again, or use CompxTray.") 'Red' }
    }
    $report | Out-File -FilePath $out -Encoding UTF8
    Write-Host "`nReport saved to $out"
    if ($trayPath) { Start-Process $trayPath; Write-Host "CompxTray restarted." }
}
