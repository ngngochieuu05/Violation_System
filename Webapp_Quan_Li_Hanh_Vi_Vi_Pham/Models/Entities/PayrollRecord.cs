using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class PayrollRecord
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal KpiBonus { get; set; }
    public decimal ViolationDeduction { get; set; }
    public decimal NetSalary { get; set; }
    public string Status { get; set; } = "Chưa thanh toán"; // Chưa thanh toán, Đã thanh toán
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
