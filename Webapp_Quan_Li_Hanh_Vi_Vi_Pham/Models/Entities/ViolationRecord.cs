namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class ViolationRecord
{
    public Guid Id { get; set; }
    public string TrackingId { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public string CameraLocation { get; set; } = string.Empty;
    public string EvidenceUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool TelegramSent { get; set; }
    public bool TelegramPhotoSent { get; set; }
    public DateTime? TelegramSentAtUtc { get; set; }
    public string? TelegramDeliveryMode { get; set; }
    public string? TelegramTargetChatIds { get; set; }
    public string? TelegramLastError { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewChannel { get; set; }
    public string? ReviewNote { get; set; }
}
