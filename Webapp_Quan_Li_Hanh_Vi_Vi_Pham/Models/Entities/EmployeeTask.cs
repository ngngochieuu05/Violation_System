using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class EmployeeTask
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Done
    public DateTime CreatedAt { get; set; }
}
