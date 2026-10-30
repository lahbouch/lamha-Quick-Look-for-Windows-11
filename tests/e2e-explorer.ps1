# End-to-end Explorer test with real key presses. Run it on an UNLOCKED session and do not touch the keyboard or mouse
# for about a minute:  powershell -ExecutionPolicy Bypass -File tests\e2e-explorer.ps1 [-Exe path\to\Lamha.exe]
param(
    [string]$Exe = "$env:LOCALAPPDATA\Programs\Lamha\Lamha.exe",
    [string]$Report = "$PSScriptRoot\..\docs\e2e-results.txt"
)
$ErrorActionPreference = 'Stop'
trap { "CRASHED: $_ at line $($_.InvocationInfo.ScriptLineNumber)" | Set-Content $Report -Encoding utf8; Write-Host "$_"; break }

if (Get-Process LogonUI -ErrorAction SilentlyContinue) { Write-Host "The session is locked. Unlock it and run again."; exit 2 }
if (-not (Test-Path $Exe)) { Write-Host "Not found: $Exe"; exit 2 }

Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class U {
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; public long pad; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KI ki; }
  [DllImport("user32")] public static extern uint SendInput(uint n, INPUT[] i, int size);
  [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32")] public static extern void mouse_event(uint f, int x, int y, int d, IntPtr e);
  [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(100); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  static void Key(ushort vk, bool up) { var i = new INPUT[1]; i[0].type = 1; i[0].ki.vk = vk; i[0].ki.flags = up ? 2u : 0u; SendInput(1, i, Marshal.SizeOf(typeof(INPUT))); }
  public static void Press(params ushort[] keys) { foreach (var k in keys) Key(k, false); for (int n = keys.Length - 1; n >= 0; n--) Key(keys[n], true); }
  public static IntPtr Preview(uint pid) { IntPtr found = IntPtr.Zero; EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); RECT r; GetWindowRect(h, out r); if (p == pid && IsWindowVisible(h) && r.R - r.L >= 400 && r.B - r.T >= 250) { found = h; return false; } return true; }, IntPtr.Zero); return found; }
}
"@

$VK = @{ Space = 0x20; Esc = 0x1B; Right = 0x27; Left = 0x25; F2 = 0x71; Ctrl = 0x11; Shift = 0x10; L = 0x4C; E = 0x45; T = 0x54; Tab = 0x09; S = 0x53; A = 0x41; B = 0x42; Down = 0x28 }

$dir = "$env:LOCALAPPDATA\Temp\lamha-e2e" # not $env:TEMP: it can be a short 8.3 path, which never equals what Explorer reports
$dir2 = "$dir\second"
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dir2 -Force | Out-Null
$arabicName = [string]::Join('', [char[]](0x062A,0x0642,0x0631,0x064A,0x0631)) + ".txt"
foreach ($n in 'a.txt', 'b.txt', 'c.txt', $arabicName) { Set-Content "$dir\$n" "hello" -Encoding utf8 }
Set-Content "$dir\unsupported.zip" "x"
Set-Content "$dir2\x.txt" "x" -Encoding utf8
Set-Content "$dir2\y.txt" "y" -Encoding utf8

$log = "$env:LOCALAPPDATA\Lamha\logs\lamha.log"
function LogLines { if (Test-Path $log) { @(Get-Content $log -Encoding utf8) } else { @() } }

Get-Process Lamha -ErrorAction SilentlyContinue | Stop-Process -Force
$app = Start-Process $Exe -ArgumentList '--tray' -PassThru
Start-Sleep -Seconds 2

$shell = New-Object -ComObject Shell.Application
function OpenFolder($path) {
    Start-Process explorer.exe $path
    Start-Sleep -Seconds 3
    $found = $null
    foreach ($w in @($shell.Windows())) { try { if ($w.Document.Folder.Self.Path -eq $path) { $found = $w } } catch {} }
    if (-not $found) { throw "Explorer window for $path did not appear" }
    $found
}
function SelectOnly($w, [string[]]$names) {
    $w.Document.SelectItem($w.Document.Folder.ParseName($names[0]), 1 + 4 + 8 + 16) # select, deselect others, ensure visible, focus
    foreach ($n in $names[1..($names.Length)]) { if ($n) { $w.Document.SelectItem($w.Document.Folder.ParseName($n), 1) } }
    Start-Sleep -Milliseconds 300
}
function Front($hwnd) { [U]::SetForegroundWindow([IntPtr]$hwnd) | Out-Null; Start-Sleep -Milliseconds 500 }
function Open { [bool]([U]::Preview($app.Id) -ne [IntPtr]::Zero) }
function Space { [U]::Press($VK.Space); Start-Sleep -Milliseconds 900 }

$results = New-Object Collections.ArrayList
function Check($name, [bool]$ok, $detail = '') {
    [void]$results.Add(("{0}  {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail))
    Write-Host $results[-1]
}

$w = OpenFolder $dir
$hwnd = $w.HWND
# A background script cannot reliably take the foreground, so a real click starts the run with Explorer in front.
[U]::SetWindowPos([IntPtr]$hwnd, [IntPtr]::Zero, 0, 0, 1800, 1000, 0x14) | Out-Null
Start-Sleep -Milliseconds 400
[U]::Click(1300, 10)
Start-Sleep -Milliseconds 500

# Space opens and closes
SelectOnly $w @('a.txt'); Front $hwnd
Space
Check "Space opens the preview" (Open)
$fg = [U]::GetForegroundWindow()
Check "The preview has keyboard focus" ($fg -eq [U]::Preview($app.Id))
Space
Check "Space closes the preview" (-not (Open))
Check "Focus returns to Explorer" ([U]::GetForegroundWindow() -eq [IntPtr]$hwnd)

# Escape
Space; [U]::Press($VK.Esc); Start-Sleep -Milliseconds 700
Check "Escape closes the preview" (-not (Open))

# Clicking away. A real click, because a background script is not allowed to bring Explorer to the front itself.
[U]::SetWindowPos([IntPtr]$hwnd, [IntPtr]::Zero, 0, 0, 1800, 1000, 0x14) | Out-Null # the title bar stays clear of the centred preview
Start-Sleep -Milliseconds 400
SelectOnly $w @('a.txt'); Front $hwnd
Space
$wasOpen = Open
Start-Sleep -Milliseconds 800
[U]::Click(1300, 10)
Start-Sleep -Milliseconds 800
Check "Clicking Explorer closes the preview" ($wasOpen -and -not (Open)) "wasOpen=$wasOpen"
if (Open) { Space } # keep a failure here from breaking the next test

# Multiple files
$before = (LogLines | Select-String 'preview open').Count
SelectOnly $w @('a.txt', 'b.txt', 'c.txt'); Front $hwnd
Space
foreach ($k in 'Right', 'Right', 'Right', 'Left') { [U]::Press($VK[$k]); Start-Sleep -Milliseconds 500 }
$opened = (LogLines | Select-String 'preview open').Count - $before
Check "Arrows move through 3 files and stop at the end" ($opened -eq 4) "opened=$opened"
[U]::Press($VK.Esc); Start-Sleep -Milliseconds 600

# Unicode name
SelectOnly $w @($arabicName); Front $hwnd
Space
Check "Arabic filename opens" (Open)
[U]::Press($VK.Esc); Start-Sleep -Milliseconds 600

# Places where Space must stay with Explorer
SelectOnly $w @('a.txt'); Front $hwnd
[U]::Press($VK.F2); Start-Sleep -Milliseconds 500
Space
Check "Space while renaming is left alone" (-not (Open))
[U]::Press($VK.Esc); Start-Sleep -Milliseconds 400
if (Open) { [U]::Press($VK.Esc); Start-Sleep -Milliseconds 400 }

Front $hwnd
[U]::Press($VK.Ctrl, $VK.L); Start-Sleep -Milliseconds 500
Space
Check "Space in the address bar is left alone" (-not (Open))
[U]::Press($VK.Esc); [U]::Press($VK.Esc); Start-Sleep -Milliseconds 400

Front $hwnd
[U]::Press($VK.Ctrl, $VK.E); Start-Sleep -Milliseconds 500
Space
Check "Space in the search box is left alone" (-not (Open))
[U]::Press($VK.Esc); [U]::Press($VK.Esc); Start-Sleep -Milliseconds 400

# Unsupported selection
SelectOnly $w @('unsupported.zip'); Front $hwnd
Space
Check "An unsupported file shows no preview" (-not (Open))

# Two windows
$w2 = OpenFolder $dir2
SelectOnly $w2 @('x.txt', 'y.txt'); Front $w2.HWND
$mark = (LogLines).Count
Space
$sel = (LogLines | Select-Object -Skip $mark | Select-String 'explorer selection: 2').Count
Check "The front Explorer window is the one read" ($sel -eq 1)
[U]::Press($VK.Esc); Start-Sleep -Milliseconds 600

# Tabs: same window, different selection per tab
Front $hwnd
SelectOnly $w @('a.txt'); Front $hwnd
[U]::Press($VK.Ctrl, $VK.T); Start-Sleep -Seconds 2
$tab = @($shell.Windows()) | Where-Object { $_.HWND -eq $hwnd -and $_.LocationURL -ne $w.LocationURL } | Select-Object -Last 1
if ($tab) {
    $tab.Navigate2($dir2); Start-Sleep -Seconds 2
    SelectOnly $tab @('x.txt', 'y.txt'); Front $hwnd
    $mark = (LogLines).Count
    Space
    $two = (LogLines | Select-Object -Skip $mark | Select-String 'explorer selection: 2').Count
    [U]::Press($VK.Esc); Start-Sleep -Milliseconds 600
    Front $hwnd
    [U]::Press($VK.Ctrl, $VK.Shift, $VK.Tab); Start-Sleep -Seconds 1
    $mark = (LogLines).Count
    Space
    $one = (LogLines | Select-Object -Skip $mark | Select-String 'explorer selection: 1').Count
    [U]::Press($VK.Esc); Start-Sleep -Milliseconds 600
    Check "Each tab's own selection is read" ($two -eq 1 -and $one -eq 1) "two=$two one=$one"
} else { Check "Each tab's own selection is read" $false "could not open a second tab" }

# Another application keeps its Space
$note = "$dir\note.txt"
Set-Content $note "" -NoNewline
$np = Start-Process notepad.exe $note -PassThru
Start-Sleep -Seconds 2
$nph = (Get-Process -Id $np.Id -ErrorAction SilentlyContinue).MainWindowHandle # Windows 11 Notepad can hand off and exit
if (-not $nph) { $nph = (Get-Process notepad | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1).MainWindowHandle }
Front $nph
foreach ($k in 'A', 'Space', 'B') { [U]::Press($VK[$k]); Start-Sleep -Milliseconds 300 }
Start-Sleep -Milliseconds 500
[U]::Press($VK.Ctrl, $VK.S); Start-Sleep -Seconds 1
$typed = (Get-Content $note -Raw)
Check "Space still types in Notepad" ($typed -match '^a b') "file=[$typed]"
Check "No preview appeared over Notepad" (-not (Open))
Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'note' } | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($x in @($shell.Windows())) { try { if ($x.Document.Folder.Self.Path -like "$dir*") { $x.Quit() } } catch {} }
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Start-Process $Exe -ArgumentList '--tray' # leave the app running as it was

$failed = @($results | Where-Object { $_ -like 'FAIL*' }).Count
$results + "" + "$($results.Count - $failed) of $($results.Count) passed, $(Get-Date -Format 'yyyy-MM-dd HH:mm')" | Set-Content $Report -Encoding utf8
Write-Host "`n$($results.Count - $failed) of $($results.Count) passed. Report: $Report"
exit $failed
