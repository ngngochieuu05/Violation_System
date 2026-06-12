using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public interface IViolationMonitoringOrchestrator
{
    Task<IReadOnlyCollection<ViolationAlertResult>> ProcessDetectionsAsync(
        IReadOnlyCollection<DetectionResult> detections,
        CancellationToken cancellationToken = default);

    Task<ViolationAlertResult> TriggerSmokeTestAsync(CancellationToken cancellationToken = default);
    Task<ViolationAlertResult> TriggerLeavingPositionTestAsync(CancellationToken cancellationToken = default);
}
