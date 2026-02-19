# MeasureScroll.ps1 - Comprehensive comparison of UserApp vs RefApp

$ErrorActionPreference = "Stop"

# Configuration
$UserAppDir = "g:\Antigravity_Project\NoNamePJ04\BrowserSmoothScroll"
$UserAppExe = "$UserAppDir\bin\Debug\net8.0-windows\BrowserSmoothScroll.exe"
$RefAppLnk = "C:\Users\ranco\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\SmoothScroll\SmoothScroll.lnk"
$SettingsDir = "$env:APPDATA\BrowserSmoothScroll"
$SettingsFile = "$SettingsDir\settings.json"
$LogsDir = "$SettingsDir\logs"
$TestUrl = "https://akaytruyen.com/con-duong-ba-chu/chuong-3744-noi-khong-thuoc-ve"

# ---- Helpers ----

function Set-Settings {
    param ([bool]$enabled, [bool]$debugMode)
    if (-not (Test-Path $SettingsFile)) {
        New-Item -Path $SettingsDir -ItemType Directory -Force | Out-Null
        Set-Content -Path $SettingsFile -Value "{}"
    }
    $json = Get-Content $SettingsFile -Raw | ConvertFrom-Json
    $json | Add-Member -MemberType NoteProperty -Name "Enabled" -Value $enabled -Force
    $json | Add-Member -MemberType NoteProperty -Name "DebugMode" -Value $debugMode -Force
    $json | ConvertTo-Json -Depth 10 | Set-Content -Path $SettingsFile
    Write-Host "  Settings: Enabled=$enabled, DebugMode=$debugMode"
}

function Get-LatestLog {
    Get-ChildItem -Path $LogsDir -Filter "scroll_debug_*.log" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
}

function Analyze-Log {
    param ([string]$logPath, [string]$testName)

    Write-Host "  Analyzing: $logPath"
    $lines = Get-Content $logPath

    if ($testName -eq "UserApp") {
        $events = @($lines | Where-Object { $_ -match "pass-self-injected" })
    }
    else {
        $events = @($lines | Where-Object { $_ -match "pass-external-injected" })
    }

    $count = $events.Count

    $samples = @()
    foreach ($line in $events) {
        if ($line -match "^(?<ts>\S+) .*raw=(?<delta>-?\d+)") {
            $samples += [PSCustomObject]@{
                Timestamp = [datetimeoffset]::Parse($matches["ts"])
                Delta     = [int]$matches["delta"]
            }
        }
    }

    $duration = 1
    if ($samples.Count -ge 2) {
        $window = ($samples[-1].Timestamp - $samples[0].Timestamp).TotalSeconds
        if ($window -gt 0) { $duration = $window }
    }

    $hz = 0
    if ($duration -gt 0) { $hz = $count / $duration }

    $avgDeltaAbs = 0
    if ($samples.Count -gt 0) {
        $avgDeltaAbs = ($samples | ForEach-Object { [math]::Abs($_.Delta) } |
            Measure-Object -Average).Average
    }

    # jitter
    $intervals = @()
    for ($i = 1; $i -lt $samples.Count; $i++) {
        $ms = ($samples[$i].Timestamp - $samples[$i - 1].Timestamp).TotalMilliseconds
        $intervals += $ms
    }
    $avgIntervalMs = 0; $stddevMs = 0
    if ($intervals.Count -gt 0) {
        $avgIntervalMs = ($intervals | Measure-Object -Average).Average
        $mean = $avgIntervalMs
        $variance = ($intervals | ForEach-Object { ($_ - $mean) * ($_ - $mean) } |
            Measure-Object -Average).Average
        $stddevMs = [math]::Sqrt($variance)
    }

    Write-Host "  ----------------------------------------"
    Write-Host "  Results for $testName"
    Write-Host "  ----------------------------------------"
    Write-Host "  Events:          $count"
    Write-Host "  Duration:        $([math]::Round($duration,2))s"
    Write-Host "  Frequency:       $([math]::Round($hz,2)) Hz"
    Write-Host "  Avg |Delta|:     $([math]::Round($avgDeltaAbs,2))"
    Write-Host "  Avg Interval:    $([math]::Round($avgIntervalMs,2)) ms"
    Write-Host "  Interval StdDev: $([math]::Round($stddevMs,2)) ms"
    Write-Host "  ----------------------------------------"

    return [PSCustomObject]@{
        Test     = $testName
        Events   = $count
        Duration = [math]::Round($duration, 2)
        Hz       = [math]::Round($hz, 2)
        AvgDelta = [math]::Round($avgDeltaAbs, 2)
        AvgIntMs = [math]::Round($avgIntervalMs, 2)
        JitterMs = [math]::Round($stddevMs, 2)
    }
}

# ---- C# Input Injector ----

$code = @"
using System;
using System.Runtime.InteropServices;

public class InputInjector {
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public UIntPtr dwExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint n, INPUT[] p, int cb);

    public static void ScrollBenchmark(int delta) {
        Send(delta, (UIntPtr)0x42535454);
    }

    public static void ScrollRaw(int delta) {
        Send(delta, UIntPtr.Zero);
    }

    private static void Send(int delta, UIntPtr extraInfo) {
        var input = new INPUT {
            type = 0,
            U = new INPUTUNION { mi = new MOUSEINPUT {
                mouseData = (uint)delta,
                dwFlags = 0x0800,
                dwExtraInfo = extraInfo
            }}
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void SendCtrlHome() {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);   // Ctrl down
        keybd_event(0x24, 0, 0, UIntPtr.Zero);   // Home down
        keybd_event(0x24, 0, 2, UIntPtr.Zero);   // Home up
        keybd_event(0x11, 0, 2, UIntPtr.Zero);   // Ctrl up
    }

    public static void SendF5() {
        keybd_event(0x74, 0, 0, UIntPtr.Zero);   // F5 down
        keybd_event(0x74, 0, 2, UIntPtr.Zero);   // F5 up
    }
}
"@
Add-Type -TypeDefinition $code -Language CSharp

# ---- Scroll helper ----
# $script:ScrollMode controls which injection method is used
$script:ScrollMode = "benchmark"

function Do-Scroll([int]$delta) {
    if ($script:ScrollMode -eq "benchmark") {
        [InputInjector]::ScrollBenchmark($delta)
    }
    else {
        [InputInjector]::ScrollRaw($delta)
    }
}

# ---- Realistic scroll pattern ----
function Run-ScrollPattern {
    Write-Host "  Running realistic scroll pattern (~25s)..."

    # Phase 1 - Slow reading (single notch every 400-600ms)
    Write-Host "    Phase 1/5: Slow reading"
    for ($i = 0; $i -lt 12; $i++) {
        Do-Scroll -120
        $delay = Get-Random -Minimum 350 -Maximum 650
        Start-Sleep -Milliseconds $delay
    }

    # Phase 2 - Fast scan down (rapid notches 40-80ms apart)
    Write-Host "    Phase 2/5: Fast scan down"
    for ($i = 0; $i -lt 20; $i++) {
        Do-Scroll -120
        $delay = Get-Random -Minimum 30 -Maximum 90
        Start-Sleep -Milliseconds $delay
    }
    Start-Sleep -Seconds 2

    # Phase 3 - Short bursts up (2 notches, pause, repeat)
    Write-Host "    Phase 3/5: Short bursts up"
    for ($i = 0; $i -lt 5; $i++) {
        Do-Scroll 120
        Start-Sleep -Milliseconds 70
        Do-Scroll 120
        $delay = Get-Random -Minimum 600 -Maximum 1200
        Start-Sleep -Milliseconds $delay
    }

    # Phase 4 - Fling down (many rapid notches)
    Write-Host "    Phase 4/5: Fling down"
    for ($i = 0; $i -lt 25; $i++) {
        Do-Scroll -120
        $delay = Get-Random -Minimum 15 -Maximum 40
        Start-Sleep -Milliseconds $delay
    }
    Start-Sleep -Seconds 3

    # Phase 5 - Slow reading up
    Write-Host "    Phase 5/5: Slow reading up"
    for ($i = 0; $i -lt 8; $i++) {
        Do-Scroll 120
        $delay = Get-Random -Minimum 400 -Maximum 700
        Start-Sleep -Milliseconds $delay
    }
    Start-Sleep -Seconds 3
}

# ---- Main ----

Write-Host "============================================"
Write-Host " BrowserSmoothScroll - Comprehensive Test"
Write-Host "============================================"
Stop-Process -Name "BrowserSmoothScroll" -ErrorAction SilentlyContinue
Stop-Process -Name "SmoothScroll" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

if (Test-Path $SettingsFile) {
    Copy-Item $SettingsFile "$SettingsFile.bak" -Force
}

try {
    # -- Test 1: User App --
    Write-Host ""
    Write-Host "[TEST 1] User App (BrowserSmoothScroll)"
    $env:BSS_ALLOW_TEST_INJECTED = "1"
    Set-Settings -enabled $true -debugMode $true

    Start-Process -FilePath $UserAppExe
    Start-Process $TestUrl
    Write-Host "  Waiting for browser + app (8s)..."
    Start-Sleep -Seconds 6
    [InputInjector]::SendCtrlHome()
    Start-Sleep -Seconds 2

    $script:ScrollMode = "benchmark"
    Run-ScrollPattern

    Stop-Process -Name "BrowserSmoothScroll" -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $log1 = Get-LatestLog
    $res1 = Analyze-Log -logPath $log1.FullName -testName "UserApp"

    # -- Test 2: Reference App --
    Write-Host ""
    Write-Host "[TEST 2] Reference App (SmoothScroll)"
    $env:BSS_ALLOW_TEST_INJECTED = $null
    Set-Settings -enabled $false -debugMode $true

    Start-Process -FilePath $RefAppLnk
    Start-Sleep -Seconds 2
    Start-Process -FilePath $UserAppExe
    Write-Host "  Reloading page and scrolling to top..."
    Start-Sleep -Seconds 2
    [InputInjector]::SendF5()
    Start-Sleep -Seconds 4
    [InputInjector]::SendCtrlHome()
    Write-Host "  Waiting for apps (4s)..."
    Start-Sleep -Seconds 4

    $script:ScrollMode = "raw"
    Run-ScrollPattern

    Stop-Process -Name "BrowserSmoothScroll" -ErrorAction SilentlyContinue
    Stop-Process -Name "SmoothScroll" -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $log2 = Get-LatestLog
    $res2 = Analyze-Log -logPath $log2.FullName -testName "RefApp"

    # -- Summary --
    Write-Host ""
    Write-Host "========== COMPARISON SUMMARY =========="
    Write-Host ""
    $hzDiff = [math]::Round($res1.Hz - $res2.Hz, 2)
    $deltaDiff = [math]::Round($res1.AvgDelta - $res2.AvgDelta, 2)
    $intDiff = [math]::Round($res1.AvgIntMs - $res2.AvgIntMs, 2)
    $jitDiff = [math]::Round($res1.JitterMs - $res2.JitterMs, 2)

    Write-Host "           UserApp    RefApp     Diff"
    Write-Host "           -------    ------     ----"
    Write-Host "  Hz:      $($res1.Hz.ToString().PadLeft(7))    $($res2.Hz.ToString().PadLeft(7))    $($hzDiff.ToString().PadLeft(7))"
    Write-Host "  Delta:   $($res1.AvgDelta.ToString().PadLeft(7))    $($res2.AvgDelta.ToString().PadLeft(7))    $($deltaDiff.ToString().PadLeft(7))"
    Write-Host "  IntMs:   $($res1.AvgIntMs.ToString().PadLeft(7))    $($res2.AvgIntMs.ToString().PadLeft(7))    $($intDiff.ToString().PadLeft(7))"
    Write-Host "  Jitter:  $($res1.JitterMs.ToString().PadLeft(7))    $($res2.JitterMs.ToString().PadLeft(7))    $($jitDiff.ToString().PadLeft(7))"
    Write-Host ""

    $hzOk = [math]::Abs($hzDiff) -lt 10
    $deltaOk = [math]::Abs($deltaDiff) -lt 10

    if ($hzOk -and $deltaOk) {
        Write-Host "  RESULT: MATCH - Both apps scroll similarly!" -ForegroundColor Green
    }
    else {
        Write-Host "  RESULT: MISMATCH - Tuning needed." -ForegroundColor Yellow
        if (-not $hzOk) {
            if ($res1.Hz -gt $res2.Hz) {
                Write-Host "    -> UserApp too many events. Increase AnimationTickMs." -ForegroundColor Yellow
            }
            else {
                Write-Host "    -> UserApp too few events. Decrease AnimationTickMs." -ForegroundColor Yellow
            }
        }
        if (-not $deltaOk) {
            if ($res1.AvgDelta -gt $res2.AvgDelta) {
                Write-Host "    -> UserApp steps too large. Reduce StepSize or acceleration." -ForegroundColor Yellow
            }
            else {
                Write-Host "    -> UserApp steps too small. Increase StepSize or acceleration." -ForegroundColor Yellow
            }
        }
    }
    Write-Host ""
    Write-Host "========================================"
}
finally {
    if (Test-Path "$SettingsFile.bak") {
        Move-Item "$SettingsFile.bak" $SettingsFile -Force
        Write-Host "Settings restored."
    }
    $env:BSS_ALLOW_TEST_INJECTED = $null
}
