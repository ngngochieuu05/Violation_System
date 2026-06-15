using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class YoloWorkerResponse
{
    public List<DetectionResult> Detections { get; set; } = [];
    public string? AnnotatedBase64 { get; set; }
    public string? ImageMimeType { get; set; }
    public int FrameIndex { get; set; }
    public int FramesExamined { get; set; }
    public long ElapsedMs { get; set; }
    public long ModelLoadMs { get; set; }
    public bool IsMock { get; set; }
    public bool GpuAvailable { get; set; }
    public bool ModelLoadedFromCache { get; set; }
    public string? DeviceRequested { get; set; }
    public string? DeviceResolved { get; set; }
    public string? ErrorMessage { get; set; }
}
