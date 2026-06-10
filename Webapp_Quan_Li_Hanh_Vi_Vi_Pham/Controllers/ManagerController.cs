using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;
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

    // [GET] Hiển thị trang thêm biểu mẫu
    [HttpGet]
    public IActionResult CreateForm()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Manager"))
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    // [POST] Xử lý lưu biểu mẫu vào SQL Server
    [HttpPost]
    public async Task<IActionResult> CreateForm(FormTemplate form)
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Manager"))
        {
            return RedirectToAction("Login", "Auth");
        }

        if (ModelState.IsValid)
        {
            // Tự động cập nhật ngày giờ hiện tại
            form.LastUpdated = DateTime.Now;

            // Lưu vào Database
            _context.FormTemplates.Add(form);
            await _context.SaveChangesAsync();

            // Lưu thành công thì quay về trang danh sách biểu mẫu
            return RedirectToAction("Forms");
        }

        // Nếu có lỗi, hiển thị lại trang form cùng với dữ liệu đã nhập
        return View(form);
    }

    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Manager"))
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    // --- CÁC ENDPOINT LẤY DỮ LIỆU ĐỘNG ---

    public async Task<IActionResult> WorkSessions()
    {
        var sessions = await _context.WorkSessions
            .OrderByDescending(w => w.Date)
            .ToListAsync();
        return View(sessions);
    }

    public async Task<IActionResult> Approvals()
    {
        var approvals = await _context.ApprovalRequests
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync();
        return View(approvals);
    }

    public async Task<IActionResult> Messages()
    {
        var messages = await _context.EmployeeMessages
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();
        return View(messages);
    }

    public async Task<IActionResult> Forms()
    {
        var forms = await _context.FormTemplates
            .OrderByDescending(f => f.LastUpdated)
            .ToListAsync();
        return View(forms);
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