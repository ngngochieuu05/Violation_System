namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class YoloWorkerRequest
{
    public string ModelPath { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal ConfThreshold { get; set; }
    public decimal IouThreshold { get; set; }
    public string DeviceMode { get; set; } = "auto";
    public bool UseHalfPrecision { get; set; } = true;
    public int MaxFrames { get; set; } = 8;
    public int ImageSize { get; set; } = 640;
}
