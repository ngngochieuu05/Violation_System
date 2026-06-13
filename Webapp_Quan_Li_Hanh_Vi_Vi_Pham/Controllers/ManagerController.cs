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
using Microsoft.AspNetCore.SignalR;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[Authorize(Roles = "Manager")]
public partial class ManagerController : Controller
{
    private readonly IUserService _userService;
    private readonly IViolationService _violationService;
    private readonly ViolationDbContext _context;
    private readonly IHubContext<InternalChatHub> _hubContext;

    public ManagerController(IUserService userService, IViolationService violationService, ViolationDbContext context, IHubContext<InternalChatHub> hubContext)
    {
        _userService = userService;
        _violationService = violationService;
        _context = context;
        _hubContext = hubContext;
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
            .Where(u => u.Role == "Employee" && u.FaceImagePath != null && u.FaceImagePath != "")
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

    public class CreateEmployeeDto
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.FullName))
            return Json(new { success = false, message = "Vui lòng nhập Username và Họ Tên." });
            
        var exists = await _context.Users.AnyAsync(u => u.Username == dto.Username, cancellationToken);
        if (exists) return Json(new { success = false, message = "Tên đăng nhập đã tồn tại." });

        var defaultPassword = "123";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = dto.Username,
            FullName = dto.FullName,
            Role = "Employee",
            Department = dto.Department,
            EmployeeCode = dto.EmployeeCode,
            PasswordHash = Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.PasswordHasher.HashPassword(defaultPassword),
            FaceImagePath = string.Empty,
            ManagerKey = string.Empty,
            MustChangePassword = true,
            RequiresInitialSecuritySetup = true,
            IsKeyActivated = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, defaultPassword });
    }

    public class ResetPasswordDto
    {
        public string Username { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> ResetEmployeePassword([FromBody] ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == dto.Username && u.Role == "Employee", cancellationToken);
        if (user == null) return Json(new { success = false, message = "Không tìm thấy nhân viên." });
        
        var newPassword = "123";
        user.PasswordHash = Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.PasswordHasher.HashPassword(newPassword);
        user.MustChangePassword = true;
        user.RequiresInitialSecuritySetup = true;
        await _context.SaveChangesAsync(cancellationToken);
        
        return Json(new { success = true, newPassword });
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
    public async Task<IActionResult> ExportWorkSessionsCsv(CancellationToken cancellationToken)
    {
        var sessions = await _context.WorkSessions
            .OrderByDescending(w => w.Date)
            .ToListAsync(cancellationToken);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Ma Nhan Vien,Ngay,Gio Vao,Gio Ra,Trang Thai");

        foreach (var s in sessions)
        {
            var date = s.Date.ToString("dd/MM/yyyy");
            var checkIn = s.CheckInTime.ToString(@"hh\:mm\:ss");
            var checkOut = s.CheckOutTime?.ToString(@"hh\:mm\:ss") ?? "";
            builder.AppendLine($"{s.EmployeeUserId},{date},{checkIn},{checkOut},{s.Status}");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var fileBytes = bom.Concat(bytes).ToArray();

        return File(fileBytes, "text/csv", $"ChamCong_{DateTime.Now:yyyyMMddHHmmss}.csv");
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
            Action = "Duyệt vi phạm",
            Details = $"Quản lý cập nhật vi phạm {id} sang trạng thái {status}. Ghi chú: {note ?? string.Empty}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = "Thành công"
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
            
            var employeeGroup = InternalChatHub.BuildUserIdGroup(req.EmployeeUserId.ToString());
            await _hubContext.Clients.Group(employeeGroup).SendAsync("ReceiveNotification", new
            {
                title = "Cập nhật đơn từ",
                message = $"Đơn từ '{req.RequestType}' của bạn đã chuyển sang trạng thái: {status}.",
                type = "request",
                url = "?tab=requests"
            }, cancellationToken);

            return Json(new { success = true });
        }
        return Json(new { success = false });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMessages(CancellationToken cancellationToken)
    {
        var managerUsername = User.Identity?.Name;
        var managerFullName = User.FindFirst("FullName")?.Value ?? managerUsername;
        var msgs = await _context.EmployeeMessages
            .Where(m =>
                (m.SenderRole == "Manager" && m.SenderName == managerFullName) ||
                (m.SenderRole != "Manager" && m.Channel == managerUsername))
            .OrderBy(m => m.SentAt)
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
        if (task == null)
        {
            return Json(new { success = false, message = "Dữ liệu nhiệm vụ không hợp lệ." });
        }

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
            p.StandardWorkingDays,
            p.ActualWorkingDays,
            p.SalaryPerDay,
            p.BaseSalary,
            p.KpiBonus,
            p.ViolationDeduction,
            p.NetSalary,
            p.Status
        });
        return Json(new { success = true, data = result });
    }

    public sealed class EditPayrollRequest
    {
        public Guid Id { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal KpiBonus { get; set; }
        public int StandardWorkingDays { get; set; }
        public int ActualWorkingDays { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> EditPayrollRecord([FromBody] EditPayrollRequest request, CancellationToken cancellationToken)
    {
        var payroll = await _context.PayrollRecords.FindAsync(new object[] { request.Id }, cancellationToken);
        if (payroll == null) return Json(new { success = false, message = "Không tìm thấy bản ghi lương." });

        payroll.BaseSalary = request.BaseSalary;
        payroll.KpiBonus = request.KpiBonus;
        payroll.StandardWorkingDays = request.StandardWorkingDays;
        payroll.ActualWorkingDays = request.ActualWorkingDays;

        // Recalculate
        payroll.SalaryPerDay = payroll.StandardWorkingDays > 0 ? (payroll.BaseSalary / payroll.StandardWorkingDays) * payroll.ActualWorkingDays : 0;
        decimal gross = payroll.SalaryPerDay + payroll.KpiBonus;
        decimal net = gross - payroll.ViolationDeduction;
        payroll.NetSalary = net < 0 ? 0 : net;

        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Cập nhật thành công." });
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
            var actualDays = await _context.WorkSessions
                .Where(w => w.EmployeeUserId == emp.Id && w.Date.Month == month && w.Date.Year == year)
                .Select(w => w.Date.Date)
                .Distinct()
                .CountAsync(cancellationToken);

            int standardDays = 22;
            decimal salaryPerDay = standardDays > 0 ? (emp.BaseSalary / standardDays) * actualDays : 0;
            
            decimal deduction = violations.Count * 50000; // Mỗi vi phạm trừ 50k
            decimal kpiBonus = 1000000; // Mặc định thưởng 1M, Manager có thể sửa sau

            decimal gross = salaryPerDay + kpiBonus;
            decimal net = gross - deduction;

            var payroll = new PayrollRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp.Id,
                Month = month,
                Year = year,
                StandardWorkingDays = standardDays,
                ActualWorkingDays = actualDays,
                SalaryPerDay = salaryPerDay,
                BaseSalary = emp.BaseSalary,
                KpiBonus = kpiBonus,
                ViolationDeduction = deduction,
                NetSalary = net < 0 ? 0 : net,
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
