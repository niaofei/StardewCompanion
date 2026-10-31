[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [ValidateSet('capture','click','key','text')][string]$Action = 'capture',
    [int]$X = 0, [int]$Y = 0, [int]$VirtualKey = 0,
    [int]$HoldMilliseconds = 80,
    [string]$OutputPath = '.local\m0-window.png',
    [string]$Text = ''
)
# Development-only UI harness. This is never used by the AI bridge or shipped as an agent tool.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
if (-not ('M0Window' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M0Window {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
  [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
}
[M0Window]::SetProcessDPIAware() | Out-Null
$process = Get-Process -Id $ProcessId
$window = $process.MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw 'Process has no game window.' }
[M0Window]::ShowWindow($window,9) | Out-Null
[M0Window]::keybd_event(18,0,0,[UIntPtr]::Zero)
[M0Window]::SetForegroundWindow($window) | Out-Null
[M0Window]::keybd_event(18,0,2,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
if ([M0Window]::GetForegroundWindow() -ne $window) { throw 'Could not focus the requested game window; refusing to capture or send input to another process.' }
$bounds = New-Object M0Window+Rect
[M0Window]::GetWindowRect($window,[ref]$bounds) | Out-Null
if ($Action -eq 'click') {
    if (-not [M0Window]::SetCursorPos($bounds.Left+$X,$bounds.Top+$Y)) { throw "SetCursorPos failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error()). Interactive desktop input is unavailable." }
    Start-Sleep -Milliseconds 150
    [M0Window]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
    [M0Window]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
} elseif ($Action -eq 'key') {
    [M0Window]::keybd_event([byte]$VirtualKey,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds $HoldMilliseconds
    [M0Window]::keybd_event([byte]$VirtualKey,0,2,[UIntPtr]::Zero)
} elseif ($Action -eq 'text') {
    if ($Text -notmatch '^[A-Za-z0-9_ .-]+$') { throw 'M0 harness text accepts only simple test labels.' }
    [Windows.Forms.SendKeys]::SendWait($Text)
} else {
    $width = $bounds.Right-$bounds.Left
    $height = $bounds.Bottom-$bounds.Top
    $bitmap = New-Object Drawing.Bitmap($width,$height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,[Drawing.Size]::new($width,$height))
        $bitmap.Save([IO.Path]::GetFullPath($OutputPath))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-Host "$OutputPath ($width x $height)"
}
