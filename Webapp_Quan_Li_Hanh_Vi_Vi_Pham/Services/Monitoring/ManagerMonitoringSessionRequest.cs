namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ManagerMonitoringSessionRequest
{
    public string SourceType { get; set; } = "webcam";
    public int CameraIndex { get; set; }
    public string? FilePath { get; set; }
}
