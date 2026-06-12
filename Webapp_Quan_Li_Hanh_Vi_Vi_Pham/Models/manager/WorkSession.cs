using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

public class WorkSession
{
    public int Id { get; set; }
    public Guid? EmployeeUserId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public string Status { get; set; } = "Pending";
    public string Notes { get; set; } = string.Empty;
    public string CheckInImagePath { get; set; } = string.Empty;
    public string CheckOutImagePath { get; set; } = string.Empty;
}
