using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class AiModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "YoloSmoking", "YoloLeaving", "Deepface"
    public string ModelPath { get; set; } = string.Empty;
    public decimal ConfThreshold { get; set; }
    public decimal IouThreshold { get; set; } // relevant for Yolo
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
