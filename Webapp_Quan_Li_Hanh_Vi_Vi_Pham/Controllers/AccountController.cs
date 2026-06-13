using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Security;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;
    private readonly ViolationDbContext _dbContext;

    public AccountController(IUserService userService, ViolationDbContext dbContext)
    {
        _userService = userService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ModelState.AddModelError("", "Vui lòng nhập tên đăng nhập và mật khẩu.");
            return View();
        }

        var user = await _userService.AuthenticateAsync(username, password, cancellationToken);
        if (user == null)
        {
            ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không đúng.");
            return View();
        }

        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) && !user.IsKeyActivated)
        {
            TempData["UsernameToActivate"] = user.Username;
            return RedirectToAction("ActivateKey", "Manager");
        }

        await SignInUserAsync(user);
        return RedirectToDashboard(user.Role);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        string username,
        string password,
        string fullName,
        string role,
        string faceImage,
        string? managerKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(faceImage))
        {
            ModelState.AddModelError("", "Vui lòng điền đầy đủ thông tin và chụp ảnh nhận diện khuôn mặt.");
            return View();
        }

        if (!PasswordPolicy.TryValidate(password, out var passwordError))
        {
            ModelState.AddModelError("", passwordError);
            return View();
        }

        var newUser = new User
        {
            Username = username,
            FullName = fullName,
            Role = role,
            ManagerKey = managerKey ?? "hieudeptraivcl",
            Email = username.Contains("@") ? username : string.Empty
        };

        try
        {
            var registered = await _userService.RegisterAsync(newUser, password, faceImage, cancellationToken);
            if (registered != null)
            {
                TempData["SuccessMessage"] = "Đăng ký thành công. Vui lòng đăng nhập.";
                return RedirectToAction(nameof(Login));
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
        }

        return View();
    }

    [HttpGet]
    public IActionResult BiometricLogin()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> BiometricLogin(string username, string faceImage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(faceImage))
        {
            return Json(new { success = false, message = "Vui lòng cung cấp tên đăng nhập và hình ảnh khuôn mặt." });
        }

        var verified = await _userService.VerifyBiometricsAsync(username, faceImage, "login", cancellationToken);
        if (!verified)
        {
            return Json(new { success = false, message = "Nhận diện khuôn mặt thất bại hoặc không khớp." });
        }

        using var db = HttpContext.RequestServices.GetRequiredService<ViolationDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user == null)
        {
            return Json(new { success = false, message = "Không tìm thấy người dùng." });
        }

        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) && !user.IsKeyActivated)
        {
            TempData["UsernameToActivate"] = user.Username;
            return Json(new { success = true, redirectUrl = Url.Action("ActivateKey", "Manager") });
        }

        await SignInUserAsync(user);
        string redirectUrl;

        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            redirectUrl = Url.Action("Index", "Admin", new { area = "Admin" }) ?? "/Admin";
        }
        else if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            redirectUrl = Url.Action("Index", "Manager") ?? "/Manager";
        }
        else
        {
            redirectUrl = Url.Action("Index", "Employee") ?? "/Employee";
        }

        return Json(new { success = true, redirectUrl });
    }


    [HttpPost]
    [AllowAnonymous]
    public IActionResult GoogleLogin(string? mode = "login", string? returnUrl = null)
    {
        var normalizedMode = string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase) ? "register" : "login";
        var redirectUrl = Url.Action(nameof(GoogleResponse), new { mode = normalizedMode, returnUrl }) ?? Url.Action(nameof(Login))!;
        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        };

        return Challenge(properties, "Google");
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleResponse(string? mode = "login", string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            TempData["ErrorMessage"] = "Đăng nhập Google thất bại.";
            return RedirectToAction(nameof(Login));
        }

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "Không thể xác định tài khoản từ Google.";
            return RedirectToAction(nameof(Login));
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "Tài khoản Google chưa được liên kết với hệ thống.";
            return RedirectToAction(nameof(Login));
        }

        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) && !user.IsKeyActivated)
        {
            TempData["UsernameToActivate"] = user.Username;
            return RedirectToAction("ActivateKey", "Manager");
        }

        var isRegisterFlow = string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase);
        var createdNow = string.Equals(User.FindFirst("GoogleAccountCreated")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        if (!isRegisterFlow && createdNow)
        {
            // Nếu người dùng nhấn "Đăng nhập" nhưng tài khoản lại chưa tồn tại (vừa được tạo ngầm),
            // ta hủy bỏ việc tạo tài khoản này, đăng xuất và yêu cầu họ sang trang Đăng ký.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync(cancellationToken);

            TempData["ErrorMessage"] = "Tài khoản Google này chưa được đăng ký. Vui lòng đăng ký trước.";
            return RedirectToAction(nameof(Register));
        }

        if (isRegisterFlow)
        {
            if (createdNow)
            {
                TempData["SuccessMessage"] = "Đăng ký bằng Google thành công. Hãy cập nhật sinh trắc học trong hồ sơ sau khi đăng nhập.";
            }
            else
            {
                TempData["SuccessMessage"] = "Tài khoản Google này đã tồn tại. Hệ thống đã đăng nhập cho bạn.";
            }
        }

        if (!isRegisterFlow && !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToDashboard(user.Role);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> VerifyCurrentUserFace(string faceImage, CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(faceImage))
        {
            return Json(new { success = false, message = "Thieu du lieu xac thuc khuon mat." });
        }

        var verified = await _userService.VerifyBiometricsAsync(username, faceImage, "attendance", cancellationToken);
        if (!verified)
        {
            return Json(new { success = false, message = "Khuôn mặt không khớp với tài khoản đang đăng nhập." });
        }

        return Json(new { success = true, message = "Xac thuc khuon mat thanh cong." });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Người dùng không hợp lệ." });
        }

        var currentUser = await _userService.GetByIdAsync(userId, cancellationToken);
        if (currentUser == null)
        {
            return Json(new { success = false, message = "Người dùng không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return Json(new { success = false, message = "Vui lòng nhập mật khẩu mới." });
        }

        if (!currentUser.MustChangePassword && string.IsNullOrWhiteSpace(oldPassword))
        {
            return Json(new { success = false, message = "Vui lòng nhập mật khẩu cũ." });
        }

        if (!PasswordPolicy.TryValidate(newPassword, out var passwordError))
        {
            return Json(new { success = false, message = passwordError });
        }

        var success = await _userService.ChangePasswordAsync(userId, oldPassword, newPassword, cancellationToken);
        if (!success)
        {
            return Json(new { success = false, message = "Mật khẩu cũ không đúng." });
        }

        return Json(new { success = true, message = "Đổi mật khẩu thành công." });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OnboardingStatus(CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Người dùng không hợp lệ." });
        }

        var status = await _userService.GetSecuritySetupStatusAsync(userId, cancellationToken);
        return Json(new
        {
            success = true,
            requiresInitialSecuritySetup = status.RequiresInitialSecuritySetup,
            mustChangePassword = status.MustChangePassword,
            hasBiometricRegistration = status.HasBiometricRegistration,
            passwordPolicyDescription = PasswordPolicy.Description
        });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UpdateFace(string faceImagesBase64, CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Người dùng không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(faceImagesBase64))
        {
            return Json(new { success = false, message = "Thiếu dữ liệu khuôn mặt." });
        }

        var success = await _userService.UpdateBiometricImageAsync(userId, faceImagesBase64, cancellationToken);
        if (!success)
        {
            return Json(new { success = false, message = "Cập nhật dữ liệu khuôn mặt thất bại. Đảm bảo ảnh rõ ràng và chỉ có 1 khuôn mặt." });
        }

        return Json(new { success = true, message = "Cập nhật dữ liệu khuôn mặt thành công." });
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> BiometricRegistrationStatus(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Json(new { success = false, hasBiometricRegistration = false, message = "Thiếu thông tin người dùng." });
        }

        var hasBiometricRegistration = await _userService.HasBiometricRegistrationAsync(username, cancellationToken);
        return Json(new
        {
            success = true,
            hasBiometricRegistration,
            message = hasBiometricRegistration
                ? "Đã có dữ liệu sinh trắc học."
                : "Bạn chưa đăng ký sinh trắc học. Vui lòng cập nhật ở Cài đặt hồ sơ."
        });
    }

    private async Task SignInUserAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("FullName", user.FullName),
            new("UserId", user.Id.ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties { IsPersistent = false };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    private IActionResult RedirectToDashboard(string? role = null)
    {
        var activeRole = role;
        if (string.IsNullOrEmpty(activeRole) && User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin")) activeRole = "Admin";
            else if (User.IsInRole("Manager")) activeRole = "Manager";
            else if (User.IsInRole("Employee")) activeRole = "Employee";
        }

        if (activeRole != null)
        {
            if (activeRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Admin", new { area = "Admin" });
            }

            if (activeRole.Equals("Manager", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Manager");
            }

            if (activeRole.Equals("Employee", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Employee");
            }
        }

        return RedirectToAction("Index", "Home");
    }
}
