using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public interface IViolationMonitoringOrchestrator
{
    Task<IReadOnlyCollection<ViolationAlertResult>> ProcessDetectionsAsync(
        IReadOnlyCollection<DetectionResult> detections,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ViolationAlertResult>> ProcessInferenceRunsAsync(
        IReadOnlyCollection<YoloInferenceRunResult> runs,
        CancellationToken cancellationToken = default);

    IReadOnlyCollection<YoloInferenceRunResult> AttachPreviewTracking(
        IReadOnlyCollection<YoloInferenceRunResult> runs);

    Task<ViolationAlertResult> TriggerSmokeTestAsync(CancellationToken cancellationToken = default);
    Task<ViolationAlertResult> TriggerLeavingPositionTestAsync(CancellationToken cancellationToken = default);
    Task<ViolationAlertResult> PublishInstantAlertAsync(
        InstantViolationAlertPayload payload,
        CancellationToken cancellationToken = default);
}
