<#
  SPIKE: runs the WPF app and the Avalonia app side by side, for comparing the two front-ends.

    ./build/demo-side-by-side.ps1            both on the simulator, running the same scripted session
    ./build/demo-side-by-side.ps1 -Linux     and the Avalonia build on Linux too, under WSLg
    ./build/demo-side-by-side.ps1 -Radio     both on your real radio interface instead (no script;
                                             they share the AIOC's audio, and either can transmit)

  Each app gets a settings file of its own in %TEMP%\pdn-win-demo, so they do not fight over
  %APPDATA%\pdn-win\settings.json; with -Radio those start as copies of your real settings.
#>
param(
    [switch] $Linux,
    [switch] $Radio
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$demo = Join-Path $env:TEMP 'pdn-win-demo'
New-Item -ItemType Directory -Force $demo | Out-Null

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DemoWin {
  public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out RECT r, int size);

  // Places the window so its visible frame (less any invisible resize border) fills the given
  // rectangle, or as near as its minimum size allows, kept on the screen: a window that cannot
  // shrink to fit keeps its left edge, or its right edge when alignRight.
  public static void Place(IntPtr h, int x, int y, int w, int ht, bool alignRight) {
    SetWindowPos(h, IntPtr.Zero, x, y, w, ht, 0x0044);
    RECT outer, visible;
    GetWindowRect(h, out outer);
    if (DwmGetWindowAttribute(h, 9, out visible, Marshal.SizeOf(typeof(RECT))) != 0) visible = outer;
    int left = visible.L - outer.L, top = visible.T - outer.T, right = outer.R - visible.R, bottom = outer.B - visible.B;
    int width = Math.Max(w, (outer.R - outer.L) - left - right);
    int vx = alignRight ? x + w - width : x;
    SetWindowPos(h, IntPtr.Zero, vx - left, y - top, width + left + right, ht + top + bottom, 0x0044);
  }
}
"@
[DemoWin]::SetProcessDPIAware() | Out-Null

Write-Host 'Building...'
dotnet build "$root\src\PdnWin\PdnWin.csproj" -c Release -v q | Out-Null
dotnet build "$root\src\PdnWin.Avalonia\PdnWin.Avalonia.csproj" -c Release -v q | Out-Null

function Settings([string] $name) {
    $path = Join-Path $demo "$name.json"
    $real = Join-Path $env:APPDATA 'pdn-win\settings.json'
    if ($Radio -and (Test-Path $real)) { Copy-Item $real $path -Force }
    elseif (-not $Radio) { Remove-Item $path -ErrorAction SilentlyContinue }
    return $path
}

$appArgs = if ($Radio) { @() } else { @('--simulate', '--demo') }

function Start-App([string] $exe, [string] $settings) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $exe
    foreach ($a in $appArgs) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.Environment['PDNWIN_SETTINGS'] = $settings
    return [System.Diagnostics.Process]::Start($psi)
}

$wpf = Start-App "$root\src\PdnWin\bin\Release\net10.0-windows\pdn-win.exe" (Settings 'wpf')
$ava = Start-App "$root\src\PdnWin.Avalonia\bin\Release\net10.0\pdn-win-avalonia.exe" (Settings 'avalonia')

# Tile them: WPF on the left half of the primary screen, Avalonia on the right. Both have a minimum
# width, so on a narrow screen they overlap a little in the middle rather than run off the edge.
# Applied for a few seconds, since each app places its own window as it starts and would otherwise win.
Add-Type -AssemblyName System.Windows.Forms
$area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$half = [int]($area.Width / 2)
$tiles = @(
    [pscustomobject]@{ Process = $wpf; Left = $area.Left; Width = $half; Right = $false },
    [pscustomobject]@{ Process = $ava; Left = $area.Left + $half; Width = $area.Width - $half; Right = $true }
)
for ($round = 0; $round -lt 20; $round++) {
    foreach ($tile in $tiles) {
        $tile.Process.Refresh()
        $handle = $tile.Process.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero) {
            [DemoWin]::ShowWindow($handle, 9) | Out-Null   # restore, in case it opened maximised
            [DemoWin]::Place($handle, $tile.Left, $area.Top, $tile.Width, $area.Height, $tile.Right)
        }
    }

    Start-Sleep -Milliseconds 250
}

if ($Linux) {
    Write-Host 'Publishing for linux-x64 and starting it under WSLg...'
    dotnet publish "$root\src\PdnWin.Avalonia\PdnWin.Avalonia.csproj" -c Release -r linux-x64 --self-contained false -o "$root\publish\linux" -v q | Out-Null
    $linuxRoot = (wsl.exe wslpath -a ($root -replace '\\', '/')).Trim()
    $linuxSettings = (wsl.exe wslpath -a ((Settings 'linux') -replace '\\', '/')).Trim()
    $argText = $appArgs -join ' '
    wsl.exe -- bash -lc "rm -rf /tmp/pdnwin-demo && cp -r '$linuxRoot/publish/linux' /tmp/pdnwin-demo && chmod +x /tmp/pdnwin-demo/pdn-win-avalonia && cd /tmp/pdnwin-demo && setsid -f env PDNWIN_SETTINGS='$linuxSettings' ./pdn-win-avalonia $argText >/tmp/pdnwin-demo.log 2>&1"
}

Write-Host "WPF on the left (pid $($wpf.Id)), Avalonia on the right (pid $($ava.Id))$(if ($Linux) { ', Linux in its own WSLg window' })."
if (-not $Radio) { Write-Host 'Each runs the same scripted session against its own simulated GB7SIM: connect, two lines, BYE.' }
