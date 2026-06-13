using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class ModelSetting
{
    public int Id { get; set; }
    public string YoloModelPath { get; set; } = string.Empty;
    public decimal YoloConfThreshold { get; set; }
    public decimal YoloIouThreshold { get; set; }
    public decimal DeepfaceConfThreshold { get; set; }
    public string DeepfaceDetectorBackend { get; set; } = "opencv";
    public bool DeepfaceAlign { get; set; } = true;
    public bool DeepfaceEnforceDetection { get; set; } = true;
    public bool IsActive { get; set; }
}
