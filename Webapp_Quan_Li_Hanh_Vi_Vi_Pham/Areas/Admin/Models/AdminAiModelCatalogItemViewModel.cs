namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Models;

public class AdminAiModelCatalogItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public decimal ConfThreshold { get; set; }
    public decimal IouThreshold { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string ModelFormat { get; set; } = string.Empty;
    public string EngineLabel { get; set; } = string.Empty;
    public double? FileSizeMb { get; set; }
    public double? Accuracy { get; set; }
    public double? MacroF1 { get; set; }
    public double? Map50 { get; set; }
    public double? Map50To95 { get; set; }
    public string EvalSplit { get; set; } = string.Empty;
    public string ArtifactSource { get; set; } = string.Empty;
    public IReadOnlyList<string> ClassNames { get; set; } = Array.Empty<string>();
}
