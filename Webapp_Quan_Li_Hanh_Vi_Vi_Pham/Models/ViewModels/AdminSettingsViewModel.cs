namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;

public class AdminSettingsViewModel
{
    public string YoloModelPath { get; set; } = string.Empty;
    public decimal YoloConfThreshold { get; set; }
    public decimal YoloIouThreshold { get; set; }
    public decimal DeepfaceConfThreshold { get; set; }
}
