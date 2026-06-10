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
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;

    public AccountController(IUserService userService)
    {
        _userService = userService;
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
                TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
                return RedirectToAction("Login");
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

        var verified = await _userService.VerifyBiometricsAsync(username, faceImage, cancellationToken);
        if (!verified)
        {
            return Json(new { success = false, message = "Nhận diện khuôn mặt thất bại hoặc không khớp." });
        }

        // Login the user without password checks (biometrics bypassed standard authentication)
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
            redirectUrl = Url.Action("Index", "Violations") ?? "/Violations";
        }
        else if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            redirectUrl = Url.Action("Index", "Manager") ?? "/Manager";
        }
        else
        {
            redirectUrl = Url.Action("Index", "Employee") ?? "/Employee";
        }
        return Json(new { success = true, redirectUrl = redirectUrl });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> VerifyCurrentUserFace(string faceImage, CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(faceImage))
        {
            return Json(new { success = false, message = "Thiếu dữ liệu xác thực khuôn mặt." });
        }

        var verified = await _userService.VerifyBiometricsAsync(username, faceImage, cancellationToken);
        if (!verified)
        {
            return Json(new { success = false, message = "Khuôn mặt không khớp với tài khoản đang đăng nhập." });
        }

        return Json(new { success = true, message = "Xác thực khuôn mặt thành công." });
    }

    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
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
        if (string.IsNullOrEmpty(activeRole))
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) activeRole = "Admin";
                else if (User.IsInRole("Manager")) activeRole = "Manager";
                else if (User.IsInRole("Employee")) activeRole = "Employee";
            }
        }

        if (activeRole != null)
        {
            if (activeRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Violations");
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
