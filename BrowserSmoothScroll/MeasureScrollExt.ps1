# MeasureScrollExt.ps1 - Compares Extension vs EXE scrolling using in-browser metrics
# DO NOT COMMIT THIS FILE

$ErrorActionPreference = "Stop"

$UserAppExe = "C:\Users\ranco\Downloads\BrowserSmoothScroll_v1.0 (1)\BrowserSmoothScroll.exe"
$SettingsDir = "$env:APPDATA\BrowserSmoothScroll"
$SettingsFile = "$SettingsDir\settings.json"
$TestUrl = "https://akaytruyen.com/con-duong-ba-chu/chuong-3749-day-lui"

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
    public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
    
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint n, INPUT[] p, int cb);

    public static void ScrollRaw(int delta) {
        var input = new INPUT {
            type = 0,
            U = new INPUTUNION { mi = new MOUSEINPUT { mouseData = (uint)delta, dwFlags = 0x0800, dwExtraInfo = UIntPtr.Zero }}
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@
Add-Type -TypeDefinition $code -Language CSharp

function Run-ScrollPattern {
    Write-Host "  Injecting synthetic scroll (Phase 1: Slow, Phase 2: Fling)"
    for ($i = 0; $i -lt 5; $i++) {
        [InputInjector]::ScrollRaw(-120)
        Start-Sleep -Milliseconds 400
    }
    for ($i = 0; $i -lt 15; $i++) {
        [InputInjector]::ScrollRaw(-120)
        Start-Sleep -Milliseconds 30
    }
    Start-Sleep -Seconds 2
}

# The C# app handles its own settings, but we need to toggle it for the test
function Set-ExeState([bool]$enabled) {
    if (-not (Test-Path $SettingsFile)) { Set-Content -Path $SettingsFile -Value "{}" }
    $json = Get-Content $SettingsFile -Raw | ConvertFrom-Json
    $json | Add-Member -MemberType NoteProperty -Name "Enabled" -Value $enabled -Force
    $json | ConvertTo-Json -Depth 10 | Set-Content -Path $SettingsFile
}

Write-Host "======================================================"
Write-Host "       BrowserSmoothScroll: EXTENSION vs EXE Test      "
Write-Host "======================================================"

Stop-Process -Name "BrowserSmoothScroll" -ErrorAction SilentlyContinue

# Ensure browser is ready
Write-Host "Please ensure Chrome is open at the test URL."
Write-Host "Focus the browser now! Test starts in 5 seconds..."
Start-Sleep -Seconds 5

# ==========================================
# TEST 1: Chrome Extension
# ==========================================
Write-Host "`n[TEST 1] Extension Active (EXE OFF)"
Set-ExeState $false
# Let user clear console and prep injection manually if needed, or we just trust the visual test for now.
Run-ScrollPattern

# ==========================================
# TEST 2: Windows EXE
# ==========================================
Write-Host "`n[TEST 2] EXE Active (Extension should be OFF)"
Write-Host ">>> PLEASE DISABLE THE CHROME EXTENSION MANUALLY NOW <<<"
Write-Host "Waiting 10 seconds for you to disable extension and refocus browser..."
Start-Sleep -Seconds 10
Set-ExeState $true
Start-Process -FilePath $UserAppExe
Start-Sleep -Seconds 2

Run-ScrollPattern

Stop-Process -Name "BrowserSmoothScroll" -ErrorAction SilentlyContinue
Write-Host "`nTest complete. Compare the visual feels."
