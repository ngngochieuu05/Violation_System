namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Security;

public static class PasswordPolicy
{
    public const string Description = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.";

    public static bool TryValidate(string? password, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errorMessage = "Vui lòng nhập mật khẩu.";
            return false;
        }

        if (password.Length < 8)
        {
            errorMessage = Description;
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasSpecial = false;

        foreach (var ch in password)
        {
            if (char.IsUpper(ch)) hasUpper = true;
            else if (char.IsLower(ch)) hasLower = true;
            else if (char.IsDigit(ch)) hasDigit = true;
            else hasSpecial = true;
        }

        if (hasUpper && hasLower && hasDigit && hasSpecial)
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = Description;
        return false;
    }
}
