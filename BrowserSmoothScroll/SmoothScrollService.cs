using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BrowserSmoothScroll;

internal sealed class SmoothScrollService : IDisposable
{
    private static bool EnableCoastMotion => true;
    private const int AnimationTickMs = 8;
    private const int MaxEmitPerTick = 420;
    private const uint HighResolutionTimerPeriodMs = 1;
    private static readonly UIntPtr InjectionSignature = unchecked((UIntPtr)0x42535353u);
    private static readonly UIntPtr BenchmarkInjectionSignature = unchecked((UIntPtr)0x42535454u);
    private static readonly bool AllowBenchmarkInjectedInput =
        string.Equals(Environment.GetEnvironmentVariable("BSS_ALLOW_TEST_INJECTED"), "1", StringComparison.Ordinal);
    private const int CoastStartDelayMs = 14;
    private const double CoastKickGain = 0.22;
    private const double CoastDragPerSecond = 15.0;
    private const double CoastActiveDragPerSecond = 18.0;
    private const double CoastMaxVelocity = 3600.0;
    private const double CoastStopVelocity = 20.0;
    private const double TailResidualBleedVelocity = 90.0;
    private const double TailResidualBleedPerSecond = 5.4;
    private const double ReleaseSeedGain = 0.92;
    private const double AccelerationStrength = 1.20;
    private const double CadenceBoostStrength = 0.55;
    private const double MaxAccelerationMultiplier = 3.50;
    private const int DebugActiveTickLogInterval = 8;
    private const int DirectionJitterWindowMs = 35;
    private const int DirectionJitterMinCombo = 2;

    private readonly Func<AppSettings> _settingsProvider;
    private readonly BrowserProcessTracker _processTracker;
    private readonly ScrollDebugLogger _debugLogger;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private readonly ManualResetEventSlim _wakeEvent = new(false);
    private readonly object _stateLock = new();
    private readonly List<ScrollImpulse> _verticalImpulses = [];
    private readonly List<ScrollImpulse> _horizontalImpulses = [];

    private Thread? _animationThread;
    private IntPtr _hookHandle;
    private bool _disposed;
    private double _verticalResidual;
    private double _horizontalResidual;
    private double _verticalCoastVelocity;
    private double _horizontalCoastVelocity;
    private double _verticalReleaseSeedRate;
    private double _horizontalReleaseSeedRate;
    private bool _verticalReleaseSeedPending;
    private bool _horizontalReleaseSeedPending;
    private long _lastVerticalInputMs = long.MinValue;
    private long _lastHorizontalInputMs = long.MinValue;
    private long _lastAnimationTimestamp;
    private int _verticalCombo;
    private int _horizontalCombo;
    private int _lastVerticalDirection;
    private int _lastHorizontalDirection;
    private long _lastVerticalDirectionMs = long.MinValue;
    private long _lastHorizontalDirectionMs = long.MinValue;
    private int _lastVerticalAccelerationDirection;
    private int _lastHorizontalAccelerationDirection;
    private int _lastVerticalEmittedDelta;
    private int _lastHorizontalEmittedDelta;
    private int _debugTickCounter;
    private bool _highResolutionTimerEnabled;

    public SmoothScrollService(
        Func<AppSettings> settingsProvider,
        BrowserProcessTracker processTracker,
        ScrollDebugLogger debugLogger)
    {
        _settingsProvider = settingsProvider;
        _processTracker = processTracker;
        _debugLogger = debugLogger;
        _hookProc = HookCallback;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Unable to install mouse hook. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        _highResolutionTimerEnabled = NativeMethods.timeBeginPeriod(HighResolutionTimerPeriodMs) == 0;
        
        _animationThread = new Thread(AnimationLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "BSS_AnimationLoop"
        };
        _animationThread.Start();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = unchecked((int)(long)wParam);
        if (message is not NativeMethods.WM_MOUSEWHEEL and not NativeMethods.WM_MOUSEHWHEEL)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var settings = _settingsProvider();
        var hookData = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var rawDelta = unchecked((short)((hookData.mouseData >> 16) & 0xFFFF));
        if (rawDelta == 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // Check for injected events first to avoid loops and ensure we log benchmarks
        // regardless of the target process.
        var horizontalFromMessage = message == NativeMethods.WM_MOUSEHWHEEL;
        var injected = (hookData.flags & NativeMethods.LLMHF_INJECTED) != 0;
        var selfInjected = injected && hookData.dwExtraInfo == InjectionSignature;
        var benchmarkInjected = AllowBenchmarkInjectedInput &&
            injected &&
            hookData.dwExtraInfo == BenchmarkInjectionSignature;

        if (benchmarkInjected)
        {
            injected = false;
            selfInjected = false;
        }

        if (settings.DebugMode)
        {
            _debugLogger.LogHookWheel(horizontalFromMessage, rawDelta, injected, selfInjected, 0, "recv");
        }

        if (injected)
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogHookWheel(
                    horizontalFromMessage,
                    rawDelta,
                    injected: true,
                    selfInjected,
                    0,
                    selfInjected ? "pass-self-injected" : "pass-external-injected");
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogSkippedWheel("foreground-window-null", 0, rawDelta);
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var pid);
        if (!settings.EnableForAllAppsByDefault && !_processTracker.IsTracked(pid))
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogSkippedWheel("not-tracked-process", pid, rawDelta);
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (!settings.Enabled)
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogHookWheel(horizontalFromMessage, rawDelta, injected, selfInjected, pid, "pass-disabled");
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var horizontal = horizontalFromMessage;
        if (!horizontalFromMessage && settings.ShiftKeyHorizontalScrolling && IsShiftPressed())
        {
            horizontal = true;
        }

        if (horizontal && !settings.HorizontalSmoothness)
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogSkippedWheel("horizontal-smoothness-disabled", pid, rawDelta);
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        QueueImpulse(rawDelta, horizontal, settings);

        if (settings.DebugMode)
        {
            _debugLogger.LogHookWheel(horizontal, rawDelta, injected: false, selfInjected: false, pid, "consume");
        }

        // Consume original wheel message and replace with synthetic animated steps.
        return (IntPtr)1;
    }

    private static bool IsShiftPressed()
    {
        return (NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
    }

    private void QueueImpulse(int rawDelta, bool horizontal, AppSettings settings)
    {
        var notchCount = rawDelta / (double)NativeMethods.WHEEL_DELTA;
        var targetDelta = notchCount * settings.StepSize;
        if (settings.ReverseWheelDirection)
        {
            targetDelta = -targetDelta;
        }

        if (Math.Abs(targetDelta) < double.Epsilon)
        {
            return;
        }

        var nowMs = Environment.TickCount64;
        var direction = Math.Sign(targetDelta);
        if (IsDirectionJitter(horizontal, direction, nowMs))
        {
            if (settings.DebugMode)
            {
                _debugLogger.LogSkippedWheel("direction-jitter", 0, rawDelta);
            }

            return;
        }

        RegisterDirection(horizontal, direction, nowMs);

        var acceleration = GetAccelerationFactor(horizontal, direction, nowMs, settings);
        var acceleratedTargetDelta = targetDelta * acceleration.Factor;
        var durationMs = ComputeDurationMs(settings.AnimationTimeMs, acceleration.Factor);
        var easingPower = Math.Clamp(settings.TailToHeadRatio, 1, 10);

        var impulse = new ScrollImpulse(
            acceleratedTargetDelta,
            Stopwatch.GetTimestamp(),
            durationMs,
            settings.AnimationEasing ? EasingFunction.EaseOut : EasingFunction.Linear,
            easingPower);

        var coastVelocityKick = (acceleratedTargetDelta / Math.Max(1.0, durationMs)) * 1000.0 * CoastKickGain;
        var releaseSeedRate = Math.Abs(acceleratedTargetDelta) / Math.Max(1.0, durationMs) * 1000.0;

        if (settings.DebugMode)
        {
            _debugLogger.LogImpulse(
                horizontal,
                rawDelta,
                acceleratedTargetDelta,
                acceleration.Factor,
                durationMs,
                acceleration.Combo,
                acceleration.ComboRatio,
                acceleration.ElapsedMs,
                acceleration.CadenceRatio);
        }

        lock (_stateLock)
        {
            if (horizontal)
            {
                _horizontalImpulses.Add(impulse);
                if (EnableCoastMotion)
                {
                    _horizontalCoastVelocity = ClampAbs(_horizontalCoastVelocity + coastVelocityKick, CoastMaxVelocity);
                    _horizontalReleaseSeedRate = Math.Clamp(
                        (_horizontalReleaseSeedRate * 0.45) + (releaseSeedRate * 0.55),
                        0.0,
                        CoastMaxVelocity);
                    _horizontalReleaseSeedPending = true;
                }
            }
            else
            {
                _verticalImpulses.Add(impulse);
                if (EnableCoastMotion)
                {
                    _verticalCoastVelocity = ClampAbs(_verticalCoastVelocity + coastVelocityKick, CoastMaxVelocity);
                    _verticalReleaseSeedRate = Math.Clamp(
                        (_verticalReleaseSeedRate * 0.45) + (releaseSeedRate * 0.55),
                        0.0,
                        CoastMaxVelocity);
                    _verticalReleaseSeedPending = true;
                }
            }
            
            _wakeEvent.Set();
        }
    }

    private bool IsDirectionJitter(bool horizontal, int direction, long nowMs)
    {
        if (direction == 0)
        {
            return false;
        }

        if (horizontal)
        {
            var elapsed = _lastHorizontalDirectionMs == long.MinValue
                ? long.MaxValue
                : nowMs - _lastHorizontalDirectionMs;

            var flipDetected = _lastHorizontalDirection != 0 && direction != _lastHorizontalDirection;
            return flipDetected && elapsed <= DirectionJitterWindowMs && _horizontalCombo >= DirectionJitterMinCombo;
        }

        var verticalElapsed = _lastVerticalDirectionMs == long.MinValue
            ? long.MaxValue
            : nowMs - _lastVerticalDirectionMs;

        var verticalFlipDetected = _lastVerticalDirection != 0 && direction != _lastVerticalDirection;
        return verticalFlipDetected && verticalElapsed <= DirectionJitterWindowMs && _verticalCombo >= DirectionJitterMinCombo;
    }

    private void RegisterDirection(bool horizontal, int direction, long nowMs)
    {
        if (horizontal)
        {
            _lastHorizontalDirection = direction;
            _lastHorizontalDirectionMs = nowMs;
            return;
        }

        _lastVerticalDirection = direction;
        _lastVerticalDirectionMs = nowMs;
    }

    private AccelerationMetrics GetAccelerationFactor(bool horizontal, int direction, long nowMs, AppSettings settings)
    {
        if (settings.AccelerationDeltaMs <= 0)
        {
            return AccelerationMetrics.NoAcceleration;
        }

        if (horizontal)
        {
            if (_lastHorizontalAccelerationDirection != 0 && direction != _lastHorizontalAccelerationDirection)
            {
                _horizontalCombo = 0;
            }

            var elapsed = _lastHorizontalInputMs == long.MinValue
                ? long.MaxValue
                : nowMs - _lastHorizontalInputMs;

            if (elapsed <= settings.AccelerationDeltaMs)
            {
                _horizontalCombo = Math.Min(_horizontalCombo + 1, settings.AccelerationMax);
            }
            else
            {
                _horizontalCombo = 0;
            }

            _lastHorizontalInputMs = nowMs;
            _lastHorizontalAccelerationDirection = direction;
            return ComputeAcceleration(_horizontalCombo, elapsed, settings.AccelerationDeltaMs, settings.AccelerationMax);
        }

        if (_lastVerticalAccelerationDirection != 0 && direction != _lastVerticalAccelerationDirection)
        {
            _verticalCombo = 0;
        }

        var verticalElapsed = _lastVerticalInputMs == long.MinValue
            ? long.MaxValue
            : nowMs - _lastVerticalInputMs;

        if (verticalElapsed <= settings.AccelerationDeltaMs)
        {
            _verticalCombo = Math.Min(_verticalCombo + 1, settings.AccelerationMax);
        }
        else
        {
            _verticalCombo = 0;
        }

        _lastVerticalInputMs = nowMs;
        _lastVerticalAccelerationDirection = direction;
        return ComputeAcceleration(_verticalCombo, verticalElapsed, settings.AccelerationDeltaMs, settings.AccelerationMax);
    }

    private static AccelerationMetrics ComputeAcceleration(int combo, long elapsedMs, int accelerationWindowMs, int accelerationMax)
    {
        var safeWindow = Math.Max(1, accelerationWindowMs);
        var clampedElapsed = Math.Clamp(elapsedMs, 0, safeWindow);
        var cadenceRatio = 1.0 - (clampedElapsed / (double)safeWindow);
        var comboRatio = combo / (double)Math.Max(1, accelerationMax);
        var comboBoost = Math.Pow(Math.Clamp(comboRatio, 0.0, 1.0), 0.45) * AccelerationStrength;
        var cadenceBoost = cadenceRatio * CadenceBoostStrength;
        var factor = Math.Clamp(1.0 + comboBoost + cadenceBoost, 1.0, MaxAccelerationMultiplier);
        return new AccelerationMetrics(factor, combo, clampedElapsed, comboRatio, cadenceRatio);
    }

    private static int ComputeDurationMs(int baseDurationMs, double accelFactor)
    {
        var normalizedBase = Math.Clamp(baseDurationMs, 40, 2000);
        var accelerationImpact = Math.Max(0.0, accelFactor - 1.0);
        var shortenRatio = Math.Min(0.45, accelerationImpact * 0.11);
        var scaled = normalizedBase * (1.0 - shortenRatio);
        return Math.Max(80, (int)Math.Round(scaled));
    }

    private void AnimationLoop()
    {
        long targetTicks = 0;
        long tickIntervalTicks = (long)(Stopwatch.Frequency * AnimationTickMs / 1000.0);

        while (!_disposed)
        {
            _wakeEvent.Wait();
            if (_disposed) break;

            var startTimestamp = Stopwatch.GetTimestamp();
            if (targetTicks == 0 || startTimestamp > targetTicks + tickIntervalTicks)
            {
                targetTicks = startTimestamp;
            }

            // Execute the frame
            var hasWork = ProcessFrame(startTimestamp);

            if (!hasWork)
            {
                _wakeEvent.Reset();
                targetTicks = 0;
                continue;
            }

            // Calculate next target
            targetTicks += tickIntervalTicks;
            var currentTimestamp = Stopwatch.GetTimestamp();

            // Busy wait if close, sleep otherwise
            while (currentTimestamp < targetTicks)
            {
                var diffTicks = targetTicks - currentTimestamp;
                var diffMs = (diffTicks * 1000) / Stopwatch.Frequency;
                
                if (diffMs > 1)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(10);
                }

                currentTimestamp = Stopwatch.GetTimestamp();
            }
        }
    }

    private bool ProcessFrame(long now)
    {
        var nowMs = Environment.TickCount64;
        int verticalDeltaToSend;
        int horizontalDeltaToSend;
        int activeVerticalImpulses;
        int activeHorizontalImpulses;
        double residualVerticalSnapshot;
        double residualHorizontalSnapshot;

        lock (_stateLock)
        {
            var dtSec = ComputeDeltaSeconds(now);
            _verticalResidual += ConsumeImpulseProgress(_verticalImpulses, now);
            _horizontalResidual += ConsumeImpulseProgress(_horizontalImpulses, now);
            var verticalHasActiveImpulse = _verticalImpulses.Count > 0;
            var horizontalHasActiveImpulse = _horizontalImpulses.Count > 0;
            if (EnableCoastMotion)
            {
                ApplyCoastMotion(
                    ref _verticalResidual,
                    ref _verticalCoastVelocity,
                    ref _verticalReleaseSeedPending,
                    ref _verticalReleaseSeedRate,
                    verticalHasActiveImpulse,
                    _lastVerticalDirection,
                    _lastVerticalInputMs,
                    nowMs,
                    dtSec);
                ApplyCoastMotion(
                    ref _horizontalResidual,
                    ref _horizontalCoastVelocity,
                    ref _horizontalReleaseSeedPending,
                    ref _horizontalReleaseSeedRate,
                    horizontalHasActiveImpulse,
                    _lastHorizontalDirection,
                    _lastHorizontalInputMs,
                    nowMs,
                    dtSec);
            }
            else
            {
                _verticalCoastVelocity = 0;
                _horizontalCoastVelocity = 0;
                _verticalReleaseSeedRate = 0;
                _horizontalReleaseSeedRate = 0;
                _verticalReleaseSeedPending = false;
                _horizontalReleaseSeedPending = false;
            }

            verticalDeltaToSend = TakeIntegral(ref _verticalResidual);
            horizontalDeltaToSend = TakeIntegral(ref _horizontalResidual);
            ApplyPerTickCap(ref verticalDeltaToSend, ref _verticalResidual);
            ApplyPerTickCap(ref horizontalDeltaToSend, ref _horizontalResidual);
            ApplyDeltaSlewLimit(ref verticalDeltaToSend, ref _verticalResidual, ref _lastVerticalEmittedDelta);
            ApplyDeltaSlewLimit(ref horizontalDeltaToSend, ref _horizontalResidual, ref _lastHorizontalEmittedDelta);
            activeVerticalImpulses = _verticalImpulses.Count;
            activeHorizontalImpulses = _horizontalImpulses.Count;
            residualVerticalSnapshot = _verticalResidual;
            residualHorizontalSnapshot = _horizontalResidual;

            var hasPendingCoastWork =
                EnableCoastMotion &&
                (Math.Abs(_verticalCoastVelocity) >= CoastStopVelocity ||
                 Math.Abs(_horizontalCoastVelocity) >= CoastStopVelocity);

            var hasPendingWork =
                activeVerticalImpulses > 0 ||
                activeHorizontalImpulses > 0 ||
                hasPendingCoastWork ||
                Math.Abs(_verticalResidual) >= 1 ||
                Math.Abs(_horizontalResidual) >= 1;

            if (!hasPendingWork)
            {
                _lastAnimationTimestamp = 0;
                _verticalCombo = 0;
                _horizontalCombo = 0;
                _lastVerticalAccelerationDirection = 0;
                _lastHorizontalAccelerationDirection = 0;
                _lastVerticalEmittedDelta = 0;
                _lastHorizontalEmittedDelta = 0;
                return false;
            }
        }

        var debugMode = _settingsProvider().DebugMode;
        if (!debugMode)
        {
            _debugTickCounter = 0;
        }
        else
        {
            _debugTickCounter++;
            var shouldLogTick =
                verticalDeltaToSend != 0 ||
                horizontalDeltaToSend != 0 ||
                ((activeVerticalImpulses > 0 || activeHorizontalImpulses > 0) &&
                    (_debugTickCounter % DebugActiveTickLogInterval == 0));

            if (shouldLogTick)
            {
                _debugLogger.LogTick(
                    verticalDeltaToSend,
                    horizontalDeltaToSend,
                    residualVerticalSnapshot,
                    residualHorizontalSnapshot,
                    activeVerticalImpulses,
                    activeHorizontalImpulses);
            }
        }

        if (verticalDeltaToSend != 0)
        {
            InjectWheelDelta(verticalDeltaToSend, horizontal: false);
        }

        if (horizontalDeltaToSend != 0)
        {
            InjectWheelDelta(horizontalDeltaToSend, horizontal: true);
        }

        return true;
    }

    private double ComputeDeltaSeconds(long nowTimestamp)
    {
        if (_lastAnimationTimestamp == 0)
        {
            _lastAnimationTimestamp = nowTimestamp;
            return AnimationTickMs / 1000.0;
        }

        var dtSec = (nowTimestamp - _lastAnimationTimestamp) / (double)Stopwatch.Frequency;
        _lastAnimationTimestamp = nowTimestamp;
        return Math.Clamp(dtSec, 0.001, 0.05);
    }

    private static void ApplyCoastMotion(
        ref double residual,
        ref double velocity,
        ref bool releaseSeedPending,
        ref double releaseSeedRate,
        bool hasActiveImpulses,
        int preferredDirection,
        long lastInputMs,
        long nowMs,
        double dtSec)
    {
        if (Math.Abs(velocity) < CoastStopVelocity)
        {
            velocity = 0;
            return;
        }

        var sinceInputMs = lastInputMs == long.MinValue
            ? long.MaxValue
            : Math.Max(0, nowMs - lastInputMs);

        var coasting = sinceInputMs >= CoastStartDelayMs && !hasActiveImpulses;
        var drag = coasting ? CoastDragPerSecond : CoastActiveDragPerSecond;

        if (!hasActiveImpulses && releaseSeedPending)
        {
            var seed = Math.Clamp(releaseSeedRate * ReleaseSeedGain, 0.0, CoastMaxVelocity);
            if (seed > 0)
            {
                var direction = preferredDirection != 0
                    ? preferredDirection
                    : Math.Sign(velocity);

                if (direction == 0)
                {
                    direction = 1;
                }

                var candidate = direction * seed;
                if (Math.Abs(candidate) > Math.Abs(velocity))
                {
                    velocity = candidate;
                }
            }

            releaseSeedPending = false;
            releaseSeedRate = 0;
        }

        if (!hasActiveImpulses)
        {
            residual += velocity * dtSec;

            // Prevent endless 1-2 delta trickle once inertia gets very low.
            if (Math.Abs(velocity) <= TailResidualBleedVelocity && Math.Abs(residual) < 2.0)
            {
                residual *= Math.Exp(-TailResidualBleedPerSecond * dtSec);
            }
        }

        velocity *= Math.Exp(-drag * dtSec);
        if (Math.Abs(velocity) < CoastStopVelocity)
        {
            velocity = 0;
        }
    }

    private static double ClampAbs(double value, double maxAbs)
    {
        return Math.Clamp(value, -maxAbs, maxAbs);
    }

    private static double ConsumeImpulseProgress(List<ScrollImpulse> impulses, long now)
    {
        var emitted = 0.0;

        for (var i = impulses.Count - 1; i >= 0; i--)
        {
            emitted += impulses[i].TakeIncrement(now);

            if (impulses[i].IsCompleted)
            {
                impulses.RemoveAt(i);
            }
        }

        return emitted;
    }

    private static int TakeIntegral(ref double value)
    {
        var whole = (int)Math.Truncate(value);
        value -= whole;
        return whole;
    }

    private static void ApplyPerTickCap(ref int deltaToSend, ref double residual)
    {
        if (deltaToSend == 0)
        {
            return;
        }

        var capped = Math.Clamp(deltaToSend, -MaxEmitPerTick, MaxEmitPerTick);
        var spill = deltaToSend - capped;
        if (spill == 0)
        {
            return;
        }

        deltaToSend = capped;
        residual += spill;
    }

    private static void ApplyDeltaSlewLimit(ref int deltaToSend, ref double residual, ref int previousDelta)
    {
        if (deltaToSend == 0)
        {
            previousDelta = 0;
            return;
        }

        if (previousDelta == 0 || Math.Sign(deltaToSend) != Math.Sign(previousDelta))
        {
            previousDelta = deltaToSend;
            return;
        }

        var baseMagnitude = Math.Max(4, Math.Abs(previousDelta));
        // Allow rapid acceleration (60% increase per tick) for flings, but dampen noise.
        // No hard cap (like 18) to prevent "stalling" feeling on fast scrolls.
        var maxStep = Math.Max(2, (int)Math.Round(baseMagnitude * 0.60));
        var minAllowed = previousDelta - maxStep;
        var maxAllowed = previousDelta + maxStep;
        var limited = Math.Clamp(deltaToSend, minAllowed, maxAllowed);
        var spill = deltaToSend - limited;

        if (spill != 0)
        {
            residual += spill;
        }

        deltaToSend = limited;
        previousDelta = deltaToSend;
    }

    private static void InjectWheelDelta(int delta, bool horizontal)
    {
        var input = new NativeMethods.INPUT[]
        {
            new()
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = unchecked((uint)delta),
                        dwFlags = horizontal
                            ? NativeMethods.MOUSEEVENTF_HWHEEL
                            : NativeMethods.MOUSEEVENTF_WHEEL,
                        dwExtraInfo = InjectionSignature
                    }
                }
            }
        };

        _ = NativeMethods.SendInput((uint)input.Length, input, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wakeEvent.Set(); // Wake up thread to exit
        
        lock (_stateLock)
        {
            _lastAnimationTimestamp = 0;
            _verticalCoastVelocity = 0;
            _horizontalCoastVelocity = 0;
            _verticalReleaseSeedRate = 0;
            _horizontalReleaseSeedRate = 0;
            _verticalReleaseSeedPending = false;
            _horizontalReleaseSeedPending = false;
            _lastVerticalEmittedDelta = 0;
            _lastHorizontalEmittedDelta = 0;
            _verticalImpulses.Clear();
            _horizontalImpulses.Clear();
        }

        if (_hookHandle != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        if (_highResolutionTimerEnabled)
        {
            _ = NativeMethods.timeEndPeriod(HighResolutionTimerPeriodMs);
            _highResolutionTimerEnabled = false;
        }
        
        _wakeEvent.Dispose();
    }

    private enum EasingFunction
    {
        Linear,
        EaseOut,
        EaseIn,
        EaseInOut
    }

    private record struct AccelerationMetrics(
        double Factor,
        int Combo,
        long ElapsedMs,
        double ComboRatio,
        double CadenceRatio)
    {
        public static readonly AccelerationMetrics NoAcceleration = new(1.0, 0, 0, 0, 0);
    }

    private sealed class ScrollImpulse
    {
        private readonly double _totalDelta;
        private readonly long _creationTimestamp;
        private readonly double _durationMs;
        private readonly EasingFunction _easingFunction;
        private readonly double _easingPower;
        private double _emittedDelta;

        public ScrollImpulse(
            double totalDelta,
            long creationTimestamp,
            int durationMs,
            EasingFunction easingFunction,
            double easingPower)
        {
            _totalDelta = totalDelta;
            _creationTimestamp = creationTimestamp;
            _durationMs = durationMs;
            _easingFunction = easingFunction;
            _easingPower = easingPower;
        }

        public bool IsCompleted => Math.Abs(_emittedDelta - _totalDelta) < 0.001;

        public double TakeIncrement(long now)
        {
            if (IsCompleted)
            {
                return 0;
            }

            var elapsedMs = (now - _creationTimestamp) / (double)Stopwatch.Frequency * 1000.0;
            var progress = Math.Clamp(elapsedMs / _durationMs, 0.0, 1.0);
            var easedProgress = ApplyEasing(progress, _easingFunction, _easingPower);
            var targetEmitted = _totalDelta * easedProgress;
            var increment = targetEmitted - _emittedDelta;

            _emittedDelta = targetEmitted;
            return increment;
        }

        private static double ApplyEasing(double t, EasingFunction function, double power)
        {
            return function switch
            {
                EasingFunction.Linear => t,
                EasingFunction.EaseOut => 1.0 - Math.Pow(1.0 - t, power),
                EasingFunction.EaseIn => Math.Pow(t, power),
                EasingFunction.EaseInOut => t < 0.5
                    ? 0.5 * Math.Pow(2 * t, power)
                    : 1.0 - 0.5 * Math.Pow(2 * (1 - t), power),
                _ => t
            };
        }
    }
}
