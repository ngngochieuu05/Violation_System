namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class InstantViolationAlertPayload
{
    public string RuleType { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public string CameraLocation { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string BoundingBox { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    public string SnapshotBase64 { get; set; } = string.Empty;
    public string SnapshotMimeType { get; set; } = "image/jpeg";
}
