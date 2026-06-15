using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class YoloInferenceRunResult
{
    public string ModelName { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ModelFormat { get; set; } = string.Empty;
    public decimal ConfThreshold { get; set; }
    public decimal IouThreshold { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string RuntimeSource { get; set; } = "AiModels";
    public string Engine { get; set; } = "ultralytics";
    public bool IsMockResult { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string? AnnotatedImageBase64 { get; set; }
    public string AnnotatedImageMimeType { get; set; } = "image/jpeg";
    public int FrameIndex { get; set; }
    public int FramesExamined { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public List<DetectionResult> Detections { get; set; } = [];
}
