using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class EmployeePreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool CompactMode { get; set; }
    public bool ReducedMotion { get; set; }
    public string Language { get; set; } = "vi-VN";
    public string Theme { get; set; } = "light";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
