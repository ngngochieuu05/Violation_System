namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationMonitoringOptions
{
    public const string SectionName = "ViolationMonitoring";

    public int PollingIntervalSeconds { get; set; } = 5;
    public int SmokeDetectionThresholdCount { get; set; } = 3;
    public int EmptyChairThresholdSeconds { get; set; } = 5;
    public int EmptyChairThresholdMinutes { get; set; } = 0;
    public double TrackMatchIouThreshold { get; set; } = 0.4d;
    public string CameraLocation { get; set; } = "Camera giám sát mặc định";
    public string InstantAlertEndpointUrl { get; set; } = "https://localhost:7192/api/monitoring/instant-alert";
    public string InstantAlertApiKey { get; set; } = "local-monitoring-key";
    public double SmokeAlertSeconds { get; set; } = 1.5d;
    public double EmptyDeskAlertSeconds { get; set; } = 3d;
    public string PersonLabel { get; set; } = "person";
    public string SmokeLabel { get; set; } = "Cigarette";
    public string EmptyDeskLabel { get; set; } = "un-occupied_desk";

    public TimeSpan GetEmptyChairThreshold()
    {
        if (EmptyChairThresholdSeconds > 0)
        {
            return TimeSpan.FromSeconds(EmptyChairThresholdSeconds);
        }

        return TimeSpan.FromMinutes(Math.Max(1, EmptyChairThresholdMinutes));
    }
}
