namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ManagerMonitoringSessionStartResult
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
}
