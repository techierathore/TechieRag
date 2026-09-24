<#
.SYNOPSIS
  Presses the TechieRag probe's button on the Windows head and records the result (REQ-FN-056).

.DESCRIPTION
  Launches TechieRag.Probe.exe, binds Windows UI Automation to THAT process's own top-level window
  (by process id), invokes the button by its AutomationId and reads the result labels by theirs. No
  global keyboard or mouse input is sent, so nothing lands in another window. Writes the result text
  and a screenshot of the probe's own window (PrintWindow on its handle) to -OutDir.

.EXAMPLE
  pwsh samples/TechieRag.Probe/scripts/Invoke-ProbeWindows.ps1 `
    -Exe samples/TechieRag.Probe/bin/Debug/net10.0-windows10.0.19041.0/win-x64/TechieRag.Probe.exe `
    -OutDir tests/.artifacts/probe
#>
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [int] $TimeoutMinutes = 30,
    # REQ-FN-059: press the local-model button instead with -ButtonId RunGenerateButton -StatusId GenerateStatusLabel -ResultName windows-generate -ShotName windows-generate.
    [string] $ButtonId = 'RunEmbedButton',
    [string] $StatusId = 'StatusLabel',
    [string] $ResultName = 'windows-result',
    [string] $ShotName = 'windows-probe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ProbeWin32 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$exePath = (Resolve-Path $Exe).Path
$process = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -PassThru
$A = [System.Windows.Automation.AutomationElement]
$pidCondition = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $process.Id)

function Find-ById($root, [string] $id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# The probe's own top-level window, found by its process id.
$window = $null
$deadline = (Get-Date).AddMinutes(2)
while (-not $window -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $window = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCondition)
}
if (-not $window) { throw "The probe (pid $($process.Id)) opened no window within 2 minutes." }

$button = $null
$deadline = (Get-Date).AddMinutes(1)
while (-not $button -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $button = Find-ById $window $ButtonId
}
if (-not $button) { throw "$ButtonId was not found in the probe's window." }

$started = Get-Date
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$status = ''
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
do {
    Start-Sleep -Seconds 2
    $status = (Find-ById $window $StatusId).Current.Name
} while ($status -notmatch '^(Done|Error)' -and (Get-Date) -lt $deadline)

$labels = 'PlatformLabel', 'ModelLabel', 'ModelRootLabel', 'StatusLabel', 'DownloadLabel', 'TopResultLabel', 'TimingsLabel', 'ResultLineLabel', 'GenerateStatusLabel', 'GeneratedSentenceLabel', 'GenerationTimingsLabel'
$lines = @("TechieRag probe, Windows head, $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') on $env:COMPUTERNAME",
           "Driven by UI Automation bound to pid $($process.Id); button invoked by AutomationId; wall time $([int]((Get-Date) - $started).TotalSeconds) s")
foreach ($id in $labels) { $lines += "${id}: $((Find-ById $window $id).Current.Name)" }
$resultFile = Join-Path $OutDir "$ResultName.txt"
$lines | Set-Content -Path $resultFile -Encoding UTF8

# Screenshot of the probe's own window through its handle.
$handle = [IntPtr]$window.Current.NativeWindowHandle
$rect = New-Object ProbeWin32+RECT
[ProbeWin32]::GetWindowRect($handle, [ref]$rect) | Out-Null
$bitmap = New-Object System.Drawing.Bitmap ([Math]::Max(1, $rect.Right - $rect.Left)), ([Math]::Max(1, $rect.Bottom - $rect.Top))
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
[ProbeWin32]::PrintWindow($handle, $hdc, 2) | Out-Null
$graphics.ReleaseHdc($hdc)
$bitmap.Save((Join-Path $OutDir "$ShotName.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $bitmap.Dispose()

$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Get-Content $resultFile
if ($status -notmatch '^Done') { exit 1 }
