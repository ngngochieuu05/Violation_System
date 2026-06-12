namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;

public class RegisterViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FaceImage { get; set; } = string.Empty;
    public string? ManagerKey { get; set; }
}
