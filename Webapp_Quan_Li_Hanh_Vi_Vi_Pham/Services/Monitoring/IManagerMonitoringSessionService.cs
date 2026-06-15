namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public interface IManagerMonitoringSessionService
{
    Task<ManagerMonitoringSessionStartResult> StartSessionAsync(
        string ownerKey,
        ManagerMonitoringSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<ManagerMonitoringSessionStatus?> GetSessionStatusAsync(
        string ownerKey,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    Task StopSessionAsync(
        string ownerKey,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}
