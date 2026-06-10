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
// Nhớ using thư mục Manager mới
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers
{
    public class ManagerController : Controller
    {
        private readonly IUserService _userService;
        private readonly ViolationDbContext _context;

        public ManagerController(IUserService userService, ViolationDbContext context)
        {
            _userService = userService;
            _context = context;
        }

        // [Các phương thức ActivateKey giữ nguyên như cũ...]

        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Manager"))
            {
                return RedirectToAction("Login", "Auth");
            }
            return View();
        }

        // --- CÁC CHỨC NĂNG MỚI CỦA MANAGER ---

        public IActionResult WorkSessions()
        {
            // TODO: Lấy danh sách WorkSession từ db
            return View();
        }

        public IActionResult Approvals()
        {
            // TODO: Lấy danh sách ApprovalRequest từ db
            return View();
        }

        public IActionResult Forms()
        {
            // TODO: Lấy danh sách FormTemplate từ db
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
}