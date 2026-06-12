using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

public class ApprovalRequest
{
    public int Id { get; set; }
    public Guid? EmployeeUserId { get; set; }
    public string EmployeeUsername { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "Chờ duyệt";
}
