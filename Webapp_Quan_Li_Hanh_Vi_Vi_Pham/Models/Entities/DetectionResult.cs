namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class DetectionResult
{
    public string Label { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string BoundingBox { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
