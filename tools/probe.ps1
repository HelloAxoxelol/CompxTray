# probe.ps1 - lists USB devices and HID interfaces and writes tools\hid-report.txt.
# Read-only: it reads feature reports and listens for input reports, but never writes to a device.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\probe.ps1
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'hid-report.txt'
$sb = New-Object System.Text.StringBuilder
function W([string]$s) { [void]$sb.AppendLine($s) }

Write-Host "Loading HID helper..."
Add-Type -Path (Join-Path $here 'HidProbe.cs')

W "=== HID probe $(Get-Date -Format s) ==="
W "OS: $([Environment]::OSVersion.VersionString) | PowerShell $($PSVersionTable.PSVersion)"
W ""

Write-Host "Listing USB devices..."
W "=== USB devices ==="
Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_*' } | Sort-Object InstanceId | ForEach-Object {
    $bus = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_BusReportedDeviceDesc' -ErrorAction SilentlyContinue).Data
    W ("{0} | {1} | {2} | bus: {3}" -f $_.InstanceId, $_.Class, $_.FriendlyName, $bus)
}
W ""

Write-Host "Inspecting HID interfaces (up to a minute)..."
W "=== HID interfaces ==="
foreach ($line in [HidProbe]::Run(3000)) { W $line }

$sb.ToString() | Out-File -FilePath $out -Encoding UTF8
Write-Host ""
Write-Host "Done. Report saved to $out" -ForegroundColor Green
Write-Host "Check it for serial numbers or anything private before sharing it."
