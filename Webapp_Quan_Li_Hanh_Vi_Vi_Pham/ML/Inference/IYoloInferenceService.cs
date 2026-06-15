using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public interface IYoloInferenceService
{
    Task<IReadOnlyCollection<DetectionResult>> GetLatestDetectionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<YoloInferenceRunResult>> RunInferenceAsync(
        string? sourcePath = null,
        IEnumerable<string>? requestedModelTypes = null,
        int? maxFrames = null,
        decimal? confThreshold = null,
        decimal? iouThreshold = null,
        string? deviceMode = null,
        int? imageSize = null,
        bool? useHalfPrecision = null,
        CancellationToken cancellationToken = default);
}
