using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

public class ManagerController : Controller
{
    private readonly IUserService _userService;
    private readonly ViolationDbContext _context;

    public ManagerController(IUserService userService, ViolationDbContext context)
    {
        _userService = userService;
        _context = context;
    }

    [HttpGet]
    public IActionResult ActivateKey()
    {
        var username = TempData["UsernameToActivate"] as string;
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToAction("Login", "Auth");
        }
        ViewBag.Username = username;
        // Keep the username in TempData for the post back
        TempData.Keep("UsernameToActivate");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ActivateKey(string username, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(key))
        {
            ModelState.AddModelError("", "Vui lòng nhập đầy đủ thông tin.");
            ViewBag.Username = username;
            return View();
        }

        var success = await _userService.ActivateManagerKeyAsync(username, key, cancellationToken);
        if (success)
        {
            // Load user to sign them in
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
            if (user != null)
            {
                await SignInUserAsync(user);
                return RedirectToAction("Index");
            }
        }

        ModelState.AddModelError("", "Khóa kích hoạt không chính xác. Vui lòng liên hệ Admin.");
        ViewBag.Username = username;
        TempData.Keep("UsernameToActivate");
        return View();
    }

    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Manager"))
        {
            return RedirectToAction("Login", "Auth");
        }

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
        var authProperties = new AuthenticationProperties { IsPersistent = true };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }
}
