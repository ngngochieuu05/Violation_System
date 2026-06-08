using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public interface IYoloInferenceService
{
    Task<IReadOnlyCollection<DetectionResult>> GetLatestDetectionsAsync(
        CancellationToken cancellationToken = default);
}
