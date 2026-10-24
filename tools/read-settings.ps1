# read-settings.ps1 - reads the configuration memory of a Compx mouse and writes tools\settings-report.txt.
# Read-only: nothing is written to the mouse. Use it to check whether a new model uses the same memory map.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\read-settings.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path (Split-Path -Parent $here) 'src\Device'
$out = Join-Path $here 'settings-report.txt'
$lines = @("=== CompxTray settings report $(Get-Date -Format s) ===")
$dev = $null
try {
    Add-Type -Path @((Join-Path $src 'CompxDevice.cs'), (Join-Path $src 'WinHid.cs'), (Join-Path $src 'ConfigMap.cs'), (Join-Path $src 'MouseConfig.cs'))
    $err = $null
    $dev = [CompxTray.Device.CompxDevice]::Open([ref]$err)
    if ($dev -eq $null) { throw "Could not open the receiver: $err" }
    Write-Host "Reading from $($dev.Name) ... (about 20 s, move the mouse first so it is awake)"
    $lines += [CompxTray.Device.ConfigMap]::Diagnose($dev)
    $lines += ""
    $lines += "=== Raw traffic ==="
    $lines += $dev.Log
    Write-Host "Done." -ForegroundColor Green
}
catch {
    $lines += "ERROR: $($_.Exception.Message)"
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($dev -ne $null) { $dev.Dispose() }
    $lines | Out-File -FilePath $out -Encoding UTF8
    Write-Host "Report saved to $out"
}
