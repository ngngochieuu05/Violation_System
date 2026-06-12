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
            ModelState.AddModelError("", "Vui long nhap ten dang nhap va mat khau.");
            return View();
        }

        var user = await _userService.AuthenticateAsync(username, password, cancellationToken);
        if (user == null)
        {
            ModelState.AddModelError("", "Ten dang nhap hoac mat khau khong dung.");
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
            ModelState.AddModelError("", "Vui long dien day du thong tin va chup anh nhan dien khuon mat.");
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
            ManagerKey = managerKey ?? "hieudeptraivcl"
        };

        try
        {
            var registered = await _userService.RegisterAsync(newUser, password, faceImage, cancellationToken);
            if (registered != null)
            {
                TempData["SuccessMessage"] = "Dang ky thanh cong. Vui long dang nhap.";
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
            return Json(new { success = false, message = "Vui long cung cap ten dang nhap va hinh anh khuon mat." });
        }

        var verified = await _userService.VerifyBiometricsAsync(username, faceImage, "login", cancellationToken);
        if (!verified)
        {
            return Json(new { success = false, message = "Nhan dien khuon mat that bai hoac khong khop." });
        }

        using var db = HttpContext.RequestServices.GetRequiredService<ViolationDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user == null)
        {
            return Json(new { success = false, message = "Khong tim thay nguoi dung." });
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
            redirectUrl = "/Admin";
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
            TempData["ErrorMessage"] = "Dang nhap Google that bai.";
            return RedirectToAction(nameof(Login));
        }

        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "Khong the xac dinh tai khoan tu Google.";
            return RedirectToAction(nameof(Login));
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["ErrorMessage"] = "Tai khoan Google chua duoc lien ket voi he thong.";
            return RedirectToAction(nameof(Login));
        }

        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) && !user.IsKeyActivated)
        {
            TempData["UsernameToActivate"] = user.Username;
            return RedirectToAction("ActivateKey", "Manager");
        }

        var isRegisterFlow = string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase);
        var createdNow = string.Equals(User.FindFirst("GoogleAccountCreated")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        if (isRegisterFlow)
        {
            TempData["SuccessMessage"] = createdNow
                ? "Dang ky bang Google thanh cong. Hay doi mat khau va bo sung khuon mat ngay khi vao dashboard."
                : "Tai khoan Google nay da ton tai. He thong da dang nhap cho ban.";
        }

        if (!isRegisterFlow && !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToDashboard(user.Role);
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
            return Json(new { success = false, message = "Khuon mat khong khop voi tai khoan dang dang nhap." });
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
            return Json(new { success = false, message = "Nguoi dung khong hop le." });
        }

        var currentUser = await _userService.GetByIdAsync(userId, cancellationToken);
        if (currentUser == null)
        {
            return Json(new { success = false, message = "Nguoi dung khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return Json(new { success = false, message = "Vui long nhap mat khau moi." });
        }

        if (!currentUser.MustChangePassword && string.IsNullOrWhiteSpace(oldPassword))
        {
            return Json(new { success = false, message = "Vui long nhap mat khau cu." });
        }

        if (!PasswordPolicy.TryValidate(newPassword, out var passwordError))
        {
            return Json(new { success = false, message = passwordError });
        }

        var success = await _userService.ChangePasswordAsync(userId, oldPassword, newPassword, cancellationToken);
        if (!success)
        {
            return Json(new { success = false, message = "Mat khau cu khong dung." });
        }

        return Json(new { success = true, message = "Doi mat khau thanh cong." });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OnboardingStatus(CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Nguoi dung khong hop le." });
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
            return Json(new { success = false, message = "Nguoi dung khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(faceImagesBase64))
        {
            return Json(new { success = false, message = "Thieu du lieu khuon mat." });
        }

        var success = await _userService.UpdateBiometricImageAsync(userId, faceImagesBase64, cancellationToken);
        if (!success)
        {
            return Json(new { success = false, message = "Cap nhat du lieu khuon mat that bai. Dam bao anh ro rang va chi co 1 khuon mat." });
        }

        return Json(new { success = true, message = "Cap nhat du lieu khuon mat thanh cong." });
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
            return Json(new { success = false, hasBiometricRegistration = false, message = "Thieu thong tin nguoi dung." });
        }

        var hasBiometricRegistration = await _userService.HasBiometricRegistrationAsync(username, cancellationToken);
        return Json(new
        {
            success = true,
            hasBiometricRegistration,
            message = hasBiometricRegistration
                ? "Da co du lieu sinh trac hoc."
                : "Ban chua dang ky sinh trac hoc. Vui long cap nhat o Cai dat ho so."
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
                return Redirect("/Admin");
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
