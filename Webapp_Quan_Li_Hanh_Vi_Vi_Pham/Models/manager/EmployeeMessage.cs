using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

public class EmployeeMessage
{
    public int Id { get; set; }
    public Guid? EmployeeUserId { get; set; }
    public string EmployeeUsername { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Channel { get; set; } = "manager";
    public string SenderRole { get; set; } = "Employee";
    public string SenderName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsRead { get; set; }
}
