namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ManagerMonitoringSessionStatus
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string State { get; set; } = "starting";
    public string Message { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? SourceLabel { get; set; }
    public bool GpuAvailable { get; set; }
    public string DeviceResolved { get; set; } = string.Empty;
    public int FrameIndex { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public string? PrimaryModelType { get; set; }
    public List<ManagerMonitoringModelOutput> Models { get; set; } = [];
}

public class ManagerMonitoringModelOutput
{
    public string ModelType { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public decimal ConfThreshold { get; set; }
    public decimal IouThreshold { get; set; }
    public bool IsMockResult { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public string? ImageUrl { get; set; }
    public int DetectionCount { get; set; }
    public List<ManagerMonitoringDetectionItem> Detections { get; set; } = [];
}

public class ManagerMonitoringDetectionItem
{
    public string Label { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string BoundingBox { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
