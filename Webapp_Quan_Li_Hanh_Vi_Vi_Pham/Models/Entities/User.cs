using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FaceImagePath { get; set; } = string.Empty;
    public string AvatarPath { get; set; } = string.Empty;
    public string ManagerKey { get; set; } = string.Empty;
    public bool IsKeyActivated { get; set; }
    public bool MustChangePassword { get; set; }
    public bool RequiresInitialSecuritySetup { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public decimal BaseSalary { get; set; } = 15000000;
    public string PayrollPin { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
