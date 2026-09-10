param([string]$WindowTitle = 'Valtrans Test Arena')
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public sealed class ArenaWindowFinder {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  public int Left, Top, Width, Height;
  public bool Found;
  public bool Find(string title) {
    Found = false;
    EnumWindows((hWnd, lParam) => {
      if (!IsWindowVisible(hWnd)) return true;
      var sb = new StringBuilder(256);
      GetWindowText(hWnd, sb, 256);
      if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) return true;
      RECT r;
      GetWindowRect(hWnd, out r);
      Left = r.Left; Top = r.Top; Width = r.Right - r.Left; Height = r.Bottom - r.Top;
      Found = true;
      return false;
    }, IntPtr.Zero);
    return Found;
  }
}
'@
$finder = New-Object ArenaWindowFinder
if (-not $finder.Find($WindowTitle)) { throw "Window not found: $WindowTitle" }
$path = Join-Path $env:LOCALAPPDATA 'Valtrans/TestArena/window-layout.json'
New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
$layout = @{
    Left = [double]$finder.Left
    Top = [double]$finder.Top
    Width = [double]$finder.Width
    Height = [double]$finder.Height
    SavedUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$layout | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
Write-Output "Arena layout saved: $path"
Write-Output ($layout | ConvertTo-Json -Compress)
