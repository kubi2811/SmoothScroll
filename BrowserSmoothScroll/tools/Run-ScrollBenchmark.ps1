param(
    [int]$Iterations = 2,
    [int]$BurstNotches = 8,
    [int]$NotchDelayMs = 16,
    [int]$PauseBetweenBurstsMs = 900,
    [switch]$Build
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectDir "bin\\Debug\\net8.0-windows\\BrowserSmoothScroll.exe"
$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$settingsPath = Join-Path $appData "BrowserSmoothScroll\\settings.json"
$logsDir = Join-Path $appData "BrowserSmoothScroll\\logs"
$proc = $null

if ($Build) {
    dotnet build $projectDir | Out-Host
}

if (-not (Test-Path $exePath)) {
    throw "Missing executable: $exePath. Run dotnet build first."
}

$originalSettings = $null
if (Test-Path $settingsPath) {
    $originalSettings = Get-Content -Path $settingsPath -Raw
}

function Set-BenchmarkSettings {
    param([string]$Path)

    $settings = [ordered]@{
        Enabled = $true
        AutoStartOnLogin = $false
        EnableForAllAppsByDefault = $true
        StepSize = 120
        AnimationTimeMs = 360
        AccelerationDeltaMs = 70
        AccelerationMax = 7
        TailToHeadRatio = 3
        AnimationEasing = $true
        ShiftKeyHorizontalScrolling = $true
        HorizontalSmoothness = $true
        ReverseWheelDirection = $false
        DebugMode = $true
        ProcessAllowList = "chrome,msedge"
    }

    if (Test-Path $Path) {
        try {
            $loaded = Get-Content -Path $Path -Raw | ConvertFrom-Json -AsHashtable
            foreach ($key in $loaded.Keys) {
                $settings[$key] = $loaded[$key]
            }
        }
        catch {
            # Keep benchmark defaults if settings file is malformed.
        }
    }

    $settings["Enabled"] = $true
    $settings["DebugMode"] = $true
    $settings["EnableForAllAppsByDefault"] = $true
    $settings["AutoStartOnLogin"] = $false

    $settingsDir = Split-Path -Parent $Path
    if (-not (Test-Path $settingsDir)) {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    }

    $settingsJson = $settings | ConvertTo-Json -Depth 6
    Set-Content -Path $Path -Value $settingsJson -Encoding UTF8
}

function Restore-Settings {
    param([string]$Path, [string]$BackupContent)

    if ($null -ne $BackupContent) {
        Set-Content -Path $Path -Value $BackupContent -Encoding UTF8
        return
    }

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Force
    }
}

function Ensure-NativeSendInput {
    if ($null -ne ("WheelBench" -as [type])) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class WheelBench
{
    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private static readonly UIntPtr BenchmarkTag = unchecked((UIntPtr)0x42535454u);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void SendWheel(int delta)
    {
        var input = new INPUT[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = unchecked((uint)delta),
                        dwFlags = MOUSEEVENTF_WHEEL,
                        dwExtraInfo = BenchmarkTag
                    }
                }
            }
        };

        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }
}
"@ | Out-Null
}

function Run-BenchmarkInput {
    param(
        [int]$IterationsCount,
        [int]$BurstCount,
        [int]$PerNotchDelayMs,
        [int]$BurstPauseMs
    )

    Ensure-NativeSendInput

    for ($iteration = 0; $iteration -lt $IterationsCount; $iteration++) {
        for ($i = 0; $i -lt $BurstCount; $i++) {
            [WheelBench]::SendWheel(-120)
            Start-Sleep -Milliseconds $PerNotchDelayMs
        }

        Start-Sleep -Milliseconds $BurstPauseMs

        for ($i = 0; $i -lt [Math]::Max(4, [int]($BurstCount / 2)); $i++) {
            [WheelBench]::SendWheel(120)
            Start-Sleep -Milliseconds $PerNotchDelayMs
        }

        Start-Sleep -Milliseconds $BurstPauseMs
    }
}

function Parse-LogMetrics {
    param([string]$LogPath)

    $rawSum = 0
    $injSum = 0
    $rawCount = 0
    $injCount = 0
    $lowTickCount = 0
    $tickCount = 0

    Get-Content -Path $LogPath | ForEach-Object {
        if ($_ -match 'HOOK action=recv dir=V raw=(?<raw>-?\d+) source=(?<source>[a-z\-]+)') {
            $raw = [Math]::Abs([int]$matches.raw)
            $source = $matches.source

            if ($source -eq "raw") {
                $rawCount++
                $rawSum += $raw
            }
            elseif ($source -eq "self-injected") {
                $injCount++
                $injSum += $raw
            }
        }

        if ($_ -match 'TICK outV=(?<outV>-?\d+) .* activeV=(?<activeV>\d+)') {
            $tickCount++
            $outV = [Math]::Abs([int]$matches.outV)
            $activeV = [int]$matches.activeV
            if ($activeV -eq 0 -and $outV -le 2 -and $outV -gt 0) {
                $lowTickCount++
            }
        }
    }

    [pscustomobject]@{
        rawCount = $rawCount
        injectedCount = $injCount
        rawSum = $rawSum
        injectedSum = $injSum
        gain = if ($rawSum -gt 0) { [Math]::Round($injSum / $rawSum, 3) } else { 0 }
        lowTailTicks = $lowTickCount
        totalTicks = $tickCount
    }
}

try {
    Set-BenchmarkSettings -Path $settingsPath

    if (-not (Test-Path $logsDir)) {
        New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    }

    $before = @()
    if (Test-Path $logsDir) {
        $before = Get-ChildItem -Path $logsDir -Filter "scroll_debug_*.log" | Select-Object -ExpandProperty FullName
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = Split-Path -Parent $exePath
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["BSS_ALLOW_TEST_INJECTED"] = "1"

    $proc = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $proc) {
        throw "Failed to start BrowserSmoothScroll."
    }

    Start-Sleep -Milliseconds 800
    Run-BenchmarkInput `
        -IterationsCount $Iterations `
        -BurstCount $BurstNotches `
        -PerNotchDelayMs $NotchDelayMs `
        -BurstPauseMs $PauseBetweenBurstsMs

    Start-Sleep -Milliseconds 2400

    if (-not $proc.HasExited) {
        $proc.Kill()
        $proc.WaitForExit()
    }

    $after = Get-ChildItem -Path $logsDir -Filter "scroll_debug_*.log" | Sort-Object LastWriteTime -Descending
    $created = $after | Where-Object { $_.FullName -notin $before } | Select-Object -First 1
    if ($null -eq $created) {
        $created = $after | Select-Object -First 1
    }

    if ($null -eq $created) {
        throw "Benchmark finished but no debug log was found."
    }

    $metrics = Parse-LogMetrics -LogPath $created.FullName

    Write-Output "Benchmark log: $($created.FullName)"
    Write-Output ("rawCount={0} injectedCount={1} rawSum={2} injectedSum={3} gain={4} lowTailTicks={5}" -f `
        $metrics.rawCount,
        $metrics.injectedCount,
        $metrics.rawSum,
        $metrics.injectedSum,
        $metrics.gain,
        $metrics.lowTailTicks)
}
finally {
    if ($null -ne $proc -and -not $proc.HasExited) {
        try {
            $proc.Kill()
            $proc.WaitForExit()
        }
        catch {
        }
    }

    Restore-Settings -Path $settingsPath -BackupContent $originalSettings
}
