namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationAlertResult
{
    public Guid ViolationId { get; set; }
    public string TrackId { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool TelegramAttempted { get; set; }
}
