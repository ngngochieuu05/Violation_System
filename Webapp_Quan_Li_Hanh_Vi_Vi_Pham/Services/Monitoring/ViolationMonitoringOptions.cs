namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationMonitoringOptions
{
    public const string SectionName = "ViolationMonitoring";

    public int PollingIntervalSeconds { get; set; } = 5;
    public int SmokeDetectionThresholdCount { get; set; } = 3;
    public int EmptyChairThresholdMinutes { get; set; } = 10;
    public double TrackMatchIouThreshold { get; set; } = 0.4d;
    public string CameraLocation { get; set; } = "Camera giám sát mặc định";
}
