namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Models;

public class AdminAiModelsPageViewModel
{
    public List<AdminAiModelCatalogItemViewModel> Models { get; set; } = [];
    public int TotalModels => Models.Count;
    public int ActiveModels => Models.Count(item => item.IsActive);
    public decimal AverageConfThreshold => Models.Count == 0 ? 0m : Models.Average(item => item.ConfThreshold);
    public int ModelsWithSavedEval => Models.Count(item => !string.IsNullOrWhiteSpace(item.ArtifactSource));
}
