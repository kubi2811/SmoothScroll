using System.Text.Json.Serialization;

namespace BrowserSmoothScroll;

internal sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool AutoStartOnLogin { get; set; } = false;
    public bool EnableForAllAppsByDefault { get; set; } = false;
    public int StepSize { get; set; } = 95;
    public int AnimationTimeMs { get; set; } = 500;
    public int AccelerationDeltaMs { get; set; } = 70;
    public int AccelerationMax { get; set; } = 5;
    public int TailToHeadRatio { get; set; } = 3;
    public bool AnimationEasing { get; set; } = true;
    public bool ShiftKeyHorizontalScrolling { get; set; } = true;
    public bool HorizontalSmoothness { get; set; } = true;
    public bool ReverseWheelDirection { get; set; } = false;
    public bool DebugMode { get; set; } = false;
    public string ProcessAllowList { get; set; } = "chrome,msedge";

    [JsonIgnore]
    public string[] AllowedProcessNames =>
        ProcessAllowList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static name => name.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public AppSettings Clone() =>
        new()
        {
            Enabled = Enabled,
            AutoStartOnLogin = AutoStartOnLogin,
            EnableForAllAppsByDefault = EnableForAllAppsByDefault,
            StepSize = StepSize,
            AnimationTimeMs = AnimationTimeMs,
            AccelerationDeltaMs = AccelerationDeltaMs,
            AccelerationMax = AccelerationMax,
            TailToHeadRatio = TailToHeadRatio,
            AnimationEasing = AnimationEasing,
            ShiftKeyHorizontalScrolling = ShiftKeyHorizontalScrolling,
            HorizontalSmoothness = HorizontalSmoothness,
            ReverseWheelDirection = ReverseWheelDirection,
            DebugMode = DebugMode,
            ProcessAllowList = ProcessAllowList
        };

    public void Normalize()
    {
        StepSize = Math.Clamp(StepSize, 20, 600);
        AnimationTimeMs = Math.Clamp(AnimationTimeMs, 40, 2000);
        AccelerationDeltaMs = Math.Clamp(AccelerationDeltaMs, 0, 500);
        AccelerationMax = Math.Clamp(AccelerationMax, 1, 20);
        TailToHeadRatio = Math.Clamp(TailToHeadRatio, 1, 10);
        ProcessAllowList = string.Join(
            ",",
            AllowedProcessNames.Where(static name => !string.IsNullOrWhiteSpace(name)));

        if (string.IsNullOrWhiteSpace(ProcessAllowList))
        {
            ProcessAllowList = "chrome,msedge";
        }
    }
}
