using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[Authorize(Roles = "Manager")]
public class ManagerController : Controller
{
    private readonly IUserService _userService;
    private readonly IViolationService _violationService;
    private readonly ViolationDbContext _context;

    public ManagerController(IUserService userService, IViolationService violationService, ViolationDbContext context)
    {
        _userService = userService;
        _violationService = violationService;
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ActivateKey()
    {
        var username = TempData["UsernameToActivate"] as string;
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToAction("Login", "Account");
        }
        ViewBag.Username = username;
        TempData.Keep("UsernameToActivate");
        return View();
    }

    [AllowAnonymous]
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
        return View();
    }

    // [POST] Xử lý lưu biểu mẫu vào SQL Server
    [HttpPost]
    public async Task<IActionResult> CreateForm(FormTemplate form)
    {
        if (ModelState.IsValid)
        {
            form.LastUpdated = DateTime.Now;
            _context.FormTemplates.Add(form);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index", new { tab = "forms" });
        }
        return View(form);
    }

    public IActionResult Index()
    {
        return View();
    }

    // --- Tab Navigation Redirects ---
    public IActionResult WorkSessions() => RedirectToAction(nameof(Index), new { tab = "attendance" });
    public IActionResult Approvals() => RedirectToAction(nameof(Index), new { tab = "requests" });
    public IActionResult Messages() => RedirectToAction(nameof(Index), new { tab = "messages" });
    public IActionResult Forms() => RedirectToAction(nameof(Index), new { tab = "forms" });

    // --- API ENDPOINTS FOR SPA ---

    [HttpGet]
    public async Task<IActionResult> GetHomeStats(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var employeesCount = await _context.Users.CountAsync(u => u.Role == "Employee", cancellationToken);
        var attendanceCount = await _context.WorkSessions.CountAsync(w => w.Date.Date == today, cancellationToken);
        var violationsCount = await _context.ViolationRecords.CountAsync(v => v.DetectedAtUtc.Date == today, cancellationToken);
        var requestsCount = await _context.ApprovalRequests.CountAsync(r => r.Status == "Pending", cancellationToken);

        return Json(new {
            success = true,
            data = new {
                employees = employeesCount,
                attendance = attendanceCount,
                violations = violationsCount,
                requests = requestsCount
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllEmployees(CancellationToken cancellationToken)
    {
        var employees = await _context.Users
            .Where(u => u.Role == "Employee")
            .Select(u => new {
                u.Id,
                u.EmployeeCode,
                u.FullName,
                u.Department,
                u.Username,
                u.Role,
                u.BaseSalary
            })
            .ToListAsync(cancellationToken);
        
        return Json(new { success = true, data = employees });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllWorkSessions(CancellationToken cancellationToken)
    {
        var sessions = await _context.WorkSessions
            .OrderByDescending(w => w.Date)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Json(new { success = true, data = sessions });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllViolations(CancellationToken cancellationToken)
    {
        var violations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(100)
            .Select(v => new
            {
                v.Id,
                v.TrackingId,
                v.EmployeeCode,
                v.EmployeeName,
                v.ViolationType,
                v.CameraLocation,
                v.DetectedAtUtc,
                v.Severity,
                v.Status,
                v.ReviewedBy,
                v.ReviewedAtUtc,
                v.ReviewChannel,
                v.ReviewNote
            })
            .ToListAsync(cancellationToken);
        return Json(new { success = true, data = violations });
    }

    [HttpPost]
    public async Task<IActionResult> ReviewViolation(Guid id, string status, string? note, CancellationToken cancellationToken)
    {
        var reviewer = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Manager";
        var success = await _violationService.ReviewViolationAsync(
            id,
            status,
            reviewer,
            "ManagerDashboard",
            note,
            cancellationToken);

        if (!success)
        {
            return Json(new { success = false, message = "Không tìm thấy vi phạm cần cập nhật." });
        }

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Username = reviewer,
            Action = "Review violation",
            Details = $"Manager cap nhat vi pham {id} sang trang thai {status}. Ghi chu: {note ?? string.Empty}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = "Thanh cong"
        });
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true, message = "Da cap nhat trang thai vi pham." });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllRequests(CancellationToken cancellationToken)
    {
        var requests = await _context.ApprovalRequests
            .OrderByDescending(a => a.SubmittedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Json(new { success = true, data = requests });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateRequestStatus(int id, string status, CancellationToken cancellationToken)
    {
        var req = await _context.ApprovalRequests.FindAsync(new object[] { id }, cancellationToken);
        if (req != null)
        {
            req.Status = status;
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { success = true });
        }
        return Json(new { success = false });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMessages(CancellationToken cancellationToken)
    {
        var msgs = await _context.EmployeeMessages
            .OrderByDescending(m => m.SentAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Json(new { success = true, data = msgs });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateMessageStatus(int id, CancellationToken cancellationToken)
    {
        var msg = await _context.EmployeeMessages.FindAsync(new object[] { id }, cancellationToken);
        if (msg != null)
        {
            msg.IsRead = true;
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { success = true });
        }
        return Json(new { success = false });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllForms(CancellationToken cancellationToken)
    {
        var forms = await _context.FormTemplates
            .OrderByDescending(f => f.LastUpdated)
            .ToListAsync(cancellationToken);
        return Json(new { success = true, data = forms });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllTasks(CancellationToken cancellationToken)
    {
        var tasks = await _context.EmployeeTasks
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        
        var users = await _context.Users.ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var result = tasks.Select(t => new {
            t.Id,
            t.EmployeeId,
            EmployeeName = users.ContainsKey(t.EmployeeId) ? users[t.EmployeeId] : "Unknown",
            t.Title,
            t.Description,
            t.DueDate,
            t.Status
        });
        return Json(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> AssignTask([FromBody] EmployeeTask task, CancellationToken cancellationToken)
    {
        task.Id = Guid.NewGuid();
        task.CreatedAt = DateTime.Now;
        task.Status = "Pending";
        _context.EmployeeTasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Đã giao nhiệm vụ thành công." });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllPayrolls(int month, int year, CancellationToken cancellationToken)
    {
        var payrolls = await _context.PayrollRecords
            .Where(p => p.Month == month && p.Year == year)
            .ToListAsync(cancellationToken);

        var users = await _context.Users.ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var result = payrolls.Select(p => new {
            p.Id,
            p.EmployeeId,
            EmployeeName = users.ContainsKey(p.EmployeeId) ? users[p.EmployeeId] : "Unknown",
            p.Month,
            p.Year,
            p.BaseSalary,
            p.KpiBonus,
            p.ViolationDeduction,
            p.NetSalary,
            p.Status
        });
        return Json(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> CalculateMonthlyPayroll(int month, int year, CancellationToken cancellationToken)
    {
        var employees = await _context.Users.Where(u => u.Role == "Employee").ToListAsync(cancellationToken);
        foreach (var emp in employees)
        {
            var existing = await _context.PayrollRecords
                .FirstOrDefaultAsync(p => p.EmployeeId == emp.Id && p.Month == month && p.Year == year, cancellationToken);
            
            if (existing != null) continue;

            // Tính số lượng vi phạm trong tháng
            var violations = await _context.ViolationRecords
                .Where(v => v.EmployeeCode == emp.EmployeeCode && v.DetectedAtUtc.Month == month && v.DetectedAtUtc.Year == year)
                .ToListAsync(cancellationToken);
            
            decimal deduction = violations.Count * 50000; // Mỗi vi phạm trừ 50k
            decimal kpiBonus = 1000000; // Mặc định thưởng 1M, Manager có thể sửa sau

            var payroll = new PayrollRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp.Id,
                Month = month,
                Year = year,
                BaseSalary = emp.BaseSalary,
                KpiBonus = kpiBonus,
                ViolationDeduction = deduction,
                NetSalary = emp.BaseSalary + kpiBonus - deduction,
                Status = "Chưa thanh toán",
                CreatedAt = DateTime.Now
            };
            _context.PayrollRecords.Add(payroll);
        }
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePayrollStatus(Guid id, string status, CancellationToken cancellationToken)
    {
        var payroll = await _context.PayrollRecords.FindAsync(new object[] { id }, cancellationToken);
        if (payroll != null)
        {
            payroll.Status = status;
            if (status == "Đã thanh toán") payroll.PaidAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { success = true });
        }
        return Json(new { success = false });
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

    [HttpPost]
    public async Task<IActionResult> UploadTestVideo(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "Không có file" });
        var path = Path.Combine(Directory.GetCurrentDirectory(), "ML", "samples", "test_video.mp4");
        using (var stream = new FileStream(path, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        return Json(new { success = true, message = "Video test đã được cập nhật!" });
    }

}
