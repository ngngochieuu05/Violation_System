using Microsoft.AspNetCore.SignalR;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Text;

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
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Utilities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[Authorize(Roles = "Manager")]
public partial class ManagerController : Controller
{
    private readonly IUserService _userService;
    private readonly IViolationService _violationService;
    private readonly ViolationDbContext _context;
    private readonly ITelegramAlertService _telegramAlertService;

    private readonly IHubContext<InternalChatHub> _chatHub;
    private readonly IManagerMonitoringSessionService _monitoringSessionService;
    private readonly IYoloInferenceService _yoloInferenceService;
    private readonly IViolationMonitoringOrchestrator _monitoringOrchestrator;
    private readonly IWebHostEnvironment _environment;

    public ManagerController(
        IUserService userService, 
        IViolationService violationService, 
        ViolationDbContext context,
        ITelegramAlertService telegramAlertService,
        IHubContext<InternalChatHub> chatHub,
        IManagerMonitoringSessionService monitoringSessionService,
        IYoloInferenceService yoloInferenceService,
        IViolationMonitoringOrchestrator monitoringOrchestrator,
        IWebHostEnvironment environment)
    {
        _userService = userService;
        _violationService = violationService;
        _context = context;
        _telegramAlertService = telegramAlertService;
        _chatHub = chatHub;
        _monitoringSessionService = monitoringSessionService;
        _yoloInferenceService = yoloInferenceService;
        _monitoringOrchestrator = monitoringOrchestrator;
        _environment = environment;
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
        var employeesCount = await _context.Users.CountAsync(u => u.Role == "Employee" && !string.IsNullOrEmpty(u.FaceImagePath), cancellationToken);
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
            .Where(u => u.Role == "Employee" && !string.IsNullOrEmpty(u.FaceImagePath))
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
    public async Task<IActionResult> GetViolationAssignees(CancellationToken cancellationToken)
    {
        var employees = await _context.Users
            .Where(u => u.Role == "Employee")
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.EmployeeCode)
            .Select(u => new
            {
                u.Id,
                u.EmployeeCode,
                u.FullName,
                u.Username,
                u.Department
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
        var violationRecords = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var violations = violationRecords
            .Select(v => new
            {
                v.Id,
                v.TrackingId,
                v.EmployeeCode,
                v.EmployeeName,
                v.ViolationType,
                CameraLocation = VietnameseText.NormalizeMojibake(v.CameraLocation),
                v.DetectedAtUtc,
                v.Severity,
                v.Status,
                v.TelegramSent,
                v.TelegramSentAtUtc,
                v.TelegramTargetChatIds,
                v.TelegramLastError,
                HasEvidenceImage = !string.IsNullOrWhiteSpace(v.EvidenceUrl),
                v.ReviewedBy,
                v.ReviewedAtUtc,
                v.ReviewChannel,
                v.ReviewNote,
                v.ComplaintReason,
                v.ComplaintSubmittedAtUtc
            })
            .ToList();

        return Json(new { success = true, data = violations });
    }

    [HttpGet]
    public async Task<IActionResult> GetViolationEvidence(Guid id, CancellationToken cancellationToken)
    {
        var violation = await _context.ViolationRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (violation == null)
        {
            return Json(new { success = false, message = "Không tìm thấy vi phạm." });
        }

        var evidenceImageDataUrl = BuildEvidenceImageDataUrl(violation.EvidenceUrl);
        if (string.IsNullOrWhiteSpace(evidenceImageDataUrl))
        {
            return Json(new { success = false, message = "Không tìm thấy ảnh minh chứng." });
        }

        return Json(new { success = true, data = new { evidenceImageDataUrl } });
    }

    [HttpPost]
    public async Task<IActionResult> SendViolationTelegramTest(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var message = $"[TESTCASE TELEGRAM OA]\nManager gửi thử cảnh báo HTTP API từ tab Lịch sử vi phạm.\nThời điểm: {now:yyyy-MM-dd HH:mm:ss}";
        var result = await _telegramAlertService.SendTestMessageAsync(message, cancellationToken: cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            Username = User.Identity?.Name ?? "Manager",
            Action = "Telegram testcase",
            Details = result.ResponseSummary,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = result.Success ? "Thành công" : "Thất bại"
        });
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = result.Success,
            message = result.ResponseSummary,
            chatId = result.ChatId
        });
    }

    [HttpPost]
    public async Task<IActionResult> ReviewComplaint([FromBody] ReviewComplaintRequest req, CancellationToken cancellationToken)
    {
        if (req.ViolationId == Guid.Empty)
            return Json(new { success = false, message = "Mã vi phạm không hợp lệ." });

        var violation = await _context.ViolationRecords
            .FirstOrDefaultAsync(v => v.Id == req.ViolationId, cancellationToken);

        if (violation == null)
            return Json(new { success = false, message = "Không tìm thấy vi phạm." });

        if (string.IsNullOrWhiteSpace(violation.ComplaintReason))
            return Json(new { success = false, message = "Vi phạm này chưa có khiếu nại." });

        var reviewer = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Manager";
        var decision = req.Decision ?? "Rejected";
        var oldStatus = violation.Status;

        violation.ReviewedBy = reviewer;
        violation.ReviewedAtUtc = DateTime.UtcNow;
        violation.ReviewChannel = "ComplaintReview";
        violation.ReviewNote = req.ReviewNote;

        if (string.Equals(decision, "Accepted", StringComparison.OrdinalIgnoreCase))
            violation.Status = "Rejected"; // Cập nhật đúng logic: Chấp nhận khiếu nại -> huỷ vi phạm
        else
            violation.Status = "Approved"; // Từ chối khiếu nại -> giữ nguyên vi phạm (đã duyệt)

        // Đồng bộ lương
        if (!string.IsNullOrWhiteSpace(violation.EmployeeCode))
        {
            var employee = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeCode == violation.EmployeeCode || u.Username == violation.EmployeeCode, cancellationToken);
            if (employee != null)
            {
                var payroll = await _context.PayrollRecords.FirstOrDefaultAsync(p => p.EmployeeId == employee.Id && p.Month == violation.DetectedAtUtc.Month && p.Year == violation.DetectedAtUtc.Year, cancellationToken);
                if (payroll != null)
                {
                    if (violation.Status == "Approved" && oldStatus != "Approved")
                    {
                        payroll.ViolationDeduction += 50000;
                        payroll.NetSalary -= 50000;
                    }
                    else if (violation.Status != "Approved" && oldStatus == "Approved")
                    {
                        payroll.ViolationDeduction -= 50000;
                        payroll.NetSalary += 50000;
                    }
                }
            }
        }

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Username = reviewer,
            Action = "Xử lý khiếu nại",
            Details = $"Manager {reviewer} {(decision == "Accepted" ? "chấp nhận" : "từ chối")} khiếu nại vi phạm {violation.TrackingId}. Ghi chú: {req.ReviewNote ?? "(trống)"}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = "Thành công"
        });

        if (!string.IsNullOrWhiteSpace(violation.EmployeeCode))
        {
            var employee = await _context.Users.FirstOrDefaultAsync(
                u => u.EmployeeCode == violation.EmployeeCode || u.Username == violation.EmployeeCode,
                cancellationToken);

            if (employee != null)
            {
                var decisionVi = decision == "Accepted" ? "Chấp nhận" : "Từ chối";
                var employeeName = string.IsNullOrWhiteSpace(employee.FullName) ? employee.Username : employee.FullName;

                _context.EmployeeMessages.Add(new EmployeeMessage
                {
                    EmployeeUserId = employee.Id,
                    EmployeeUsername = employee.Username,
                    EmployeeName = employeeName,
                    Channel = "violations",
                    SenderRole = "Manager",
                    SenderName = reviewer,
                    Title = $"Kết quả khiếu nại: {violation.TrackingId}",
                    Content =
                        $"Mã vi phạm: {violation.TrackingId}\n" +
                        $"Kết quả: {decisionVi}\n" +
                        $"Người xử lý: {reviewer}\n" +
                        $"Ghi chú: {req.ReviewNote ?? "(không có ghi chú)"}",
                    SentAt = DateTime.UtcNow,
                    IsRead = false
                });

                await _context.SaveChangesAsync(cancellationToken);
                await _chatHub.Clients.Group($"user:{employee.Id}").SendAsync(
                    "ReceiveNotification",
                    $"Khiếu nại vi phạm {violation.TrackingId} của bạn đã được {decisionVi.ToLower()}.");
            }
            else
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Json(new { success = true, message = $"Đã {(decision == "Accepted" ? "chấp nhận" : "từ chối")} khiếu nại thành công." });
    }

    [HttpPost]
    public async Task<IActionResult> ResendViolationTelegram(Guid id, CancellationToken cancellationToken)
    {
        var violation = await _context.ViolationRecords.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (violation == null)
        {
            return Json(new { success = false, message = "Không tìm thấy vi phạm để gửi lại Telegram." });
        }

        var message =
            $"[ĐỒNG BỘ TỪ LỊCH SỬ VI PHẠM]\n" +
            $"Mã vi phạm: {violation.TrackingId}\n" +
            $"Loại: {violation.ViolationType}\n" +
            $"Camera: {VietnameseText.NormalizeMojibake(violation.CameraLocation)}\n" +
            $"Mức độ: {violation.Severity}\n" +
            $"Ghi nhận: {violation.DetectedAtUtc:yyyy-MM-dd HH:mm:ss}";

        var telegramResults = await _telegramAlertService.SendViolationAlertAsync(violation, message, cancellationToken);
        violation.TelegramSent = telegramResults.Any(item => item.Success);
        violation.TelegramPhotoSent = telegramResults.Any(item => item.PhotoSent);
        violation.TelegramSentAtUtc = violation.TelegramSent ? DateTime.UtcNow : null;
        violation.TelegramDeliveryMode = string.Join(", ", telegramResults
            .Select(item => item.DeliveryMode)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal));
        violation.TelegramTargetChatIds = string.Join(", ", telegramResults.Select(item => item.ChatId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal));
        violation.TelegramLastError = telegramResults.FirstOrDefault(item => !item.Success)?.ResponseSummary;

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Username = User.Identity?.Name ?? "Manager",
            Action = "Đồng bộ Telegram vi phạm",
            Details = $"Gửi lại Telegram cho vi phạm {violation.TrackingId}. Kết quả: {violation.TelegramLastError ?? "OK"}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = violation.TelegramSent ? "Thành công" : "Thất bại"
        });
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = violation.TelegramSent,
            message = violation.TelegramSent
                ? "Đã gửi lại thông báo Telegram."
                : (violation.TelegramLastError ?? "Gửi Telegram thất bại."),
            telegramSent = violation.TelegramSent,
            telegramSentAtUtc = violation.TelegramSentAtUtc,
            telegramTargetChatIds = violation.TelegramTargetChatIds,
            telegramLastError = violation.TelegramLastError
        });
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

    [HttpPost]
    public async Task<IActionResult> ReviewViolationAssignment(Guid id, string status, Guid? employeeId, string? note, CancellationToken cancellationToken)
    {
        var reviewer = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Manager";
        var violation = await _context.ViolationRecords.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (violation == null)
        {
            return Json(new { success = false, message = "Không tìm thấy vi phạm cần cập nhật." });
        }

        User? employee = null;
        if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            if (employeeId is null || employeeId == Guid.Empty)
            {
                return Json(new { success = false, message = "Vui lòng chọn nhân viên vi phạm trước khi duyệt." });
            }

            employee = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == employeeId && u.Role == "Employee", cancellationToken);

            if (employee == null)
            {
                return Json(new { success = false, message = "Nhân viên được chọn không tồn tại trong hệ thống." });
            }

            violation.EmployeeCode = !string.IsNullOrWhiteSpace(employee.EmployeeCode)
                ? employee.EmployeeCode
                : employee.Username;
            violation.EmployeeName = !string.IsNullOrWhiteSpace(employee.FullName)
                ? employee.FullName
                : employee.Username;
        }

        var oldStatus = violation.Status;
        violation.Status = status;
        violation.ReviewedBy = reviewer;
        violation.ReviewedAtUtc = DateTime.UtcNow;
        violation.ReviewChannel = "ManagerDashboard";
        violation.ReviewNote = note;

        // Đồng bộ với bảng lương
        if (employee != null)
        {
            var payroll = await _context.PayrollRecords.FirstOrDefaultAsync(p => p.EmployeeId == employee.Id && p.Month == violation.DetectedAtUtc.Month && p.Year == violation.DetectedAtUtc.Year, cancellationToken);
            if (payroll != null)
            {
                if (status == "Approved" && oldStatus != "Approved")
                {
                    payroll.ViolationDeduction += 50000;
                    payroll.NetSalary -= 50000;
                }
                else if (status != "Approved" && oldStatus == "Approved")
                {
                    payroll.ViolationDeduction -= 50000;
                    payroll.NetSalary += 50000;
                }
            }
        }

        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Username = reviewer,
            Action = "Duyệt vi phạm",
            Details = $"Quản lý cập nhật vi phạm {id} sang trạng thái {status}. Nhân viên: {violation.EmployeeName} ({violation.EmployeeCode}). Ghi chú: {note ?? string.Empty}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Status = "Thành công"
        });

        if (employee != null)
        {
            _context.EmployeeMessages.Add(BuildViolationNotificationMessage(employee, violation, reviewer));
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (employee != null)
        {
            await NotifyViolationReviewedAsync(employee, violation);
        }

        return Json(new { success = true, message = "Đã cập nhật trạng thái vi phạm." });
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
        var employees = await _context.Users.Where(u => u.Role == "Employee" && !string.IsNullOrEmpty(u.FaceImagePath)).ToListAsync(cancellationToken);
        foreach (var emp in employees)
        {
            var existing = await _context.PayrollRecords
                .FirstOrDefaultAsync(p => p.EmployeeId == emp.Id && p.Month == month && p.Year == year, cancellationToken);
            
            if (existing != null) continue;

            // Tính số lượng vi phạm đã duyệt (Approved) trong tháng
            var violationsCount = await _context.ViolationRecords
                .CountAsync(v => v.EmployeeCode == emp.EmployeeCode && v.Status == "Approved" && v.DetectedAtUtc.Month == month && v.DetectedAtUtc.Year == year, cancellationToken);
            
            decimal deduction = violationsCount * 50000; // Mỗi vi phạm trừ 50k
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

    [HttpPost]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user != null)
        {
            user.PasswordHash = Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.PasswordHasher.HashPassword("123456");
            user.MustChangePassword = true;
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Không tìm thấy nhân viên" });
    }

    [HttpPost]
    public async Task<IActionResult> AddEmployee([FromBody] AddEmployeeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.EmployeeCode))
        {
            return Json(new { success = false, message = "Thiếu thông tin bắt buộc" });
        }
        var existing = await _context.Users.AnyAsync(u => u.Username == request.Username || u.EmployeeCode == request.EmployeeCode, cancellationToken);
        if (existing)
        {
            return Json(new { success = false, message = "Username hoặc Mã NV đã tồn tại" });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            FullName = request.FullName ?? string.Empty,
            Department = request.Department ?? string.Empty,
            EmployeeCode = request.EmployeeCode,
            PasswordHash = Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.PasswordHasher.HashPassword(string.IsNullOrEmpty(request.Password) ? "123456" : request.Password),
            Role = "Employee",
            CreatedAtUtc = DateTime.UtcNow,
            MustChangePassword = true,
            RequiresInitialSecuritySetup = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
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

    [HttpGet]
    public async Task<IActionResult> GetActiveAiModels(CancellationToken cancellationToken)
    {
        var models = await _context.AiModels
            .Where(m => m.IsActive)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Type,
                m.ModelPath,
                m.ConfThreshold,
                m.IouThreshold,
                modelFormat = Path.GetExtension(m.ModelPath ?? string.Empty)
            })
            .ToListAsync(cancellationToken);

        return Json(new
        {
            success = true,
            data = models,
            diagnostics = new
            {
                appsettingsFallback = "YoloModel chỉ là cấu hình dự phòng khi chưa có model YOLO active trong AiModels.",
                supportsOnnx = true,
                supportedFormats = new[] { ".pt", ".onnx" }
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> StartMonitoringSession(
        string sourceType,
        int? cameraIndex,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        string? filePath = null;
        if (!string.Equals(sourceType, "webcam", StringComparison.OrdinalIgnoreCase))
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Vui lòng chọn file video hoặc ảnh để khởi chạy giám sát." });
            }

            var sampleDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ML", "samples", "manager-tests");
            Directory.CreateDirectory(sampleDirectory);
            var extension = Path.GetExtension(file.FileName);
            var fileName = $"manager_stream_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
            filePath = Path.Combine(sampleDirectory, fileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream, cancellationToken);
        }

        var ownerKey = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "manager";
        var result = await _monitoringSessionService.StartSessionAsync(
            ownerKey,
            new ManagerMonitoringSessionRequest
            {
                SourceType = string.Equals(sourceType, "image", StringComparison.OrdinalIgnoreCase) ? "image" :
                    string.Equals(sourceType, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "webcam",
                CameraIndex = Math.Max(0, cameraIndex ?? 0),
                FilePath = filePath
            },
            cancellationToken);

        return Json(new
        {
            success = result.Success,
            message = result.Message,
            sessionId = result.SessionId,
            sourceType = result.SourceType
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMonitoringSessionStatus(string? sessionId, CancellationToken cancellationToken)
    {
        var ownerKey = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "manager";
        var status = await _monitoringSessionService.GetSessionStatusAsync(ownerKey, sessionId, cancellationToken);
        if (status == null)
        {
            return Json(new { success = false, message = "Chưa có phiên giám sát đang chạy." });
        }

        return Json(status);
    }

    [HttpPost]
    public async Task<IActionResult> StopMonitoringSession(string? sessionId, CancellationToken cancellationToken)
    {
        var ownerKey = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "manager";
        await _monitoringSessionService.StopSessionAsync(ownerKey, sessionId, cancellationToken);
        return Json(new { success = true, message = "Đã dừng phiên giám sát." });
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeMonitoringFrame(
        IFormFile frame,
        string? modelType,
        CancellationToken cancellationToken)
    {
        if (frame == null || frame.Length == 0)
        {
            return Json(new { success = false, message = "Không nhận được frame thật từ trình duyệt." });
        }

        if (!frame.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "Frame gửi lên phải là ảnh JPEG/PNG được capture từ camera hoặc video." });
        }

        var frameDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ML", "samples", "manager-frames");
        Directory.CreateDirectory(frameDirectory);
        CleanupOldMonitoringFrames(frameDirectory);

        var extension = frame.ContentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var frameName = $"browser_frame_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{extension}";
        var framePath = Path.Combine(frameDirectory, frameName);

        await using (var stream = new FileStream(framePath, FileMode.Create))
        {
            await frame.CopyToAsync(stream, cancellationToken);
        }

        var requestedTypes = string.IsNullOrWhiteSpace(modelType) || string.Equals(modelType, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : new[] { modelType };

        IReadOnlyCollection<YoloInferenceRunResult> inferenceRuns;
        try
        {
            inferenceRuns = await _yoloInferenceService.RunInferenceAsync(
                sourcePath: framePath,
                requestedModelTypes: requestedTypes,
                maxFrames: 1,
                confThreshold: null,
                iouThreshold: null,
                deviceMode: null,
                imageSize: null,
                useHalfPrecision: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Json(new
            {
                success = false,
                message = $"Lỗi runtime YOLO/Python: {ex.Message}",
                frameName,
                data = Array.Empty<object>()
            });
        }

        var trackedRuns = _monitoringOrchestrator.AttachPreviewTracking(inferenceRuns);
        var realRuns = trackedRuns.Where(run => !run.IsMockResult).ToList();
        if (realRuns.Count == 0)
        {
            var diagnostic = trackedRuns.Select(run => run.StatusMessage).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
            return Json(new
            {
                success = false,
                message = diagnostic ?? "Không chạy được model thật. Hãy kiểm tra model active do Admin cấu hình và runtime Python/CUDA.",
                frameName,
                diagnostics = trackedRuns.Select(run => new
                {
                    run.ModelName,
                    run.ModelType,
                    run.ModelPath,
                    run.ModelFormat,
                    run.StatusMessage,
                    run.Engine,
                    run.ElapsedMilliseconds
                }),
                data = Array.Empty<object>()
            });
        }

        var alertResults = Array.Empty<ViolationAlertResult>();
        try
        {
            alertResults = (await _monitoringOrchestrator.ProcessInferenceRunsAsync(realRuns, cancellationToken)).ToArray();
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices
                .GetRequiredService<ILogger<ManagerController>>()
                .LogWarning(ex, "Manager monitoring publish flow failed for frame {FrameName}", frameName);
        }

        return Json(new
        {
            success = true,
            message = $"Đã phân tích frame thật từ trình duyệt bằng {realRuns.Count} model active.",
            frameName,
            alerts = alertResults.Select(result => new
            {
                result.ViolationId,
                result.TrackId,
                result.ViolationType,
                result.Severity,
                result.DetectedAtUtc,
                result.TelegramAttempted
            }),
            data = realRuns.Select(MapRunResult)
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMonitoringSnapshot(string? modelType, int? maxFrames, int? cameraIndex, decimal? conf, decimal? iou, string? device, int? imgsz, bool? half, CancellationToken cancellationToken)
    {
        var requestedTypes = string.IsNullOrWhiteSpace(modelType) || string.Equals(modelType, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : new[] { modelType };
        var sourcePath = cameraIndex.HasValue ? $"camera:{Math.Max(0, cameraIndex.Value)}" : null;
        var inferenceRuns = await _yoloInferenceService.RunInferenceAsync(
            sourcePath: sourcePath,
            requestedModelTypes: requestedTypes,
            maxFrames: maxFrames,
            confThreshold: conf,
            iouThreshold: iou,
            deviceMode: device,
            imageSize: imgsz,
            useHalfPrecision: half,
            cancellationToken: cancellationToken);
        var trackedRuns = _monitoringOrchestrator.AttachPreviewTracking(inferenceRuns);
        var alertResults = Array.Empty<ViolationAlertResult>();
        try
        {
            alertResults = (await _monitoringOrchestrator.ProcessInferenceRunsAsync(
                trackedRuns.Where(run => !run.IsMockResult).ToList(),
                cancellationToken)).ToArray();
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices
                .GetRequiredService<ILogger<ManagerController>>()
                .LogWarning(ex, "Manager snapshot publish flow failed.");
        }

        return Json(new
        {
            success = true,
            message = "Đã tải ảnh giám sát có bounding box và tracking ID từ model active.",
            alerts = alertResults.Select(result => new
            {
                result.ViolationId,
                result.TrackId,
                result.ViolationType,
                result.Severity,
                result.DetectedAtUtc,
                result.TelegramAttempted
            }),
            data = trackedRuns.Select(MapRunResult)
        });
    }

    [HttpPost]
    public async Task<IActionResult> RunVideoTest(IFormFile file, string? modelType, int? maxFrames, decimal? conf, decimal? iou, string? device, int? imgsz, bool? half, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return Json(new { success = false, message = "Không có file đầu vào để test." });
        }

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mov", ".mkv", ".webm", ".jpg", ".jpeg", ".png", ".bmp", ".webp"
        };

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
        {
            return Json(new { success = false, message = "Chỉ hỗ trợ video hoặc ảnh phổ biến để test." });
        }

        var sampleDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ML", "samples", "manager-tests");
        Directory.CreateDirectory(sampleDirectory);

        var fileName = $"manager_test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(sampleDirectory, fileName);

        await using (var stream = new FileStream(path, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var requestedTypes = string.IsNullOrWhiteSpace(modelType) || string.Equals(modelType, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : new[] { modelType };
        var inferenceRuns = await _yoloInferenceService.RunInferenceAsync(
            path,
            requestedTypes,
            maxFrames,
            conf,
            iou,
            device,
            imgsz,
            half,
            cancellationToken);
        var trackedRuns = _monitoringOrchestrator.AttachPreviewTracking(inferenceRuns);
        var alertResults = Array.Empty<ViolationAlertResult>();
        try
        {
            alertResults = (await _monitoringOrchestrator.ProcessInferenceRunsAsync(
                trackedRuns.Where(run => !run.IsMockResult).ToList(),
                cancellationToken)).ToArray();
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices
                .GetRequiredService<ILogger<ManagerController>>()
                .LogWarning(ex, "Manager video test publish flow failed for {FileName}", file.FileName);
        }

        return Json(new
        {
            success = true,
            message = "Đã chạy test đồng thời các model active trên nguồn đầu vào đã chọn.",
            source = $"/ML/samples/manager-tests/{fileName}",
            fileName = file.FileName,
            alerts = alertResults.Select(result => new
            {
                result.ViolationId,
                result.TrackId,
                result.ViolationType,
                result.Severity,
                result.DetectedAtUtc,
                result.TelegramAttempted
            }),
            data = trackedRuns.Select(MapRunResult)
        });
    }

    private object MapRunResult(YoloInferenceRunResult run)
    {
        var annotatedImageUrl = PersistMonitoringImage(run);

        return new
        {
            run.ModelName,
            run.ModelType,
            run.ModelPath,
            run.ModelFormat,
            run.ConfThreshold,
            run.IouThreshold,
            run.SourcePath,
            run.RuntimeSource,
            run.Engine,
            run.IsMockResult,
            run.StatusMessage,
            run.FrameIndex,
            run.FramesExamined,
            run.ElapsedMilliseconds,
            run.AnnotatedImageBase64,
            run.AnnotatedImageMimeType,
            annotatedImageUrl,
            imageUrl = annotatedImageUrl,
            detectionCount = run.Detections.Count,
            detections = run.Detections.Select(detection => new
            {
                detection.ModelType,
                detection.Label,
                detection.DisplayLabel,
                detection.Confidence,
                detection.BoundingBox,
                detection.TrackId,
                processedAtUtc = detection.ProcessedAtUtc
            })
        };
    }

    private string? PersistMonitoringImage(YoloInferenceRunResult run)
    {
        if (string.IsNullOrWhiteSpace(run.AnnotatedImageBase64))
        {
            return null;
        }

        try
        {
            var extension = run.AnnotatedImageMimeType.Contains("svg", StringComparison.OrdinalIgnoreCase)
                ? ".svg"
                : ".jpg";
            var directory = Path.Combine(_environment.WebRootPath, "evidence", "monitoring");
            Directory.CreateDirectory(directory);

            var safeModelType = string.IsNullOrWhiteSpace(run.ModelType) ? "yolo" : run.ModelType.ToLowerInvariant();
            var fileName = $"{safeModelType}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(directory, fileName);

            if (extension == ".svg")
            {
                var svgContent = Encoding.UTF8.GetString(Convert.FromBase64String(run.AnnotatedImageBase64));
                System.IO.File.WriteAllText(physicalPath, svgContent, Encoding.UTF8);
            }
            else
            {
                var bytes = Convert.FromBase64String(run.AnnotatedImageBase64);
                System.IO.File.WriteAllBytes(physicalPath, bytes);
            }

            CleanupOldMonitoringImages(directory);
            return $"/evidence/monitoring/{fileName}";
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupOldMonitoringFrames(string directory)
    {
        try
        {
            var expirationUtc = DateTime.UtcNow.AddMinutes(-10);
            foreach (var file in new DirectoryInfo(directory).GetFiles().Where(file => file.LastWriteTimeUtc < expirationUtc))
            {
                file.Delete();
            }
        }
        catch
        {
        }
    }

    private static void CleanupOldMonitoringImages(string directory)
    {
        try
        {
            var expirationUtc = DateTime.UtcNow.AddMinutes(-30);
            foreach (var file in new DirectoryInfo(directory).GetFiles().Where(file => file.LastWriteTimeUtc < expirationUtc))
            {
                file.Delete();
            }
        }
        catch
        {
        }
    }

    private string ResolveMonitoringModelPath(AiModel model)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.ModelPath))
        {
            candidates.Add(model.ModelPath);
            candidates.Add(model.ModelPath.Replace("/", "\\", StringComparison.Ordinal));
        }

        var knownFallback = model.Type switch
        {
            "YoloSmoking" => @"D:\WEB\model_trained\ML\Model_trained\smoke_v1_yolov8\train_yolov8n_200ep\weights\best.pt",
            "YoloLeaving" => @"D:\WEB\model_trained\ML\Model_trained\rc_v1_yolov8\train_yolov8n_200ep\weights\best.pt",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(knownFallback))
        {
            candidates.Add(knownFallback);
        }

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeMonitoringWindowsPath(candidate);
            var absolutePath = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(_environment.ContentRootPath, normalized);
            if (System.IO.File.Exists(absolutePath))
            {
                return absolutePath;
            }
        }

        var fallbackPath = model.ModelPath ?? string.Empty;
        return Path.IsPathRooted(fallbackPath)
            ? fallbackPath
            : Path.Combine(_environment.ContentRootPath, fallbackPath);
    }

    private static bool IsYoloRuntimeModel(AiModel model)
    {
        var extension = Path.GetExtension(model.ModelPath ?? string.Empty);
        return model.Type.StartsWith("Yolo", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMonitoringWindowsPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var cleaned = rawPath.Trim();
        cleaned = cleaned.Replace("/", "\\", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\t", "\\t", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\b", "\\b", StringComparison.Ordinal);
        if (cleaned.StartsWith("D:WEB", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Insert(2, "\\");
        }

        return cleaned;
    }

    private string BuildEvidenceImageDataUrl(string? evidenceUrl)
    {
        if (string.IsNullOrWhiteSpace(evidenceUrl) || string.IsNullOrWhiteSpace(_environment.WebRootPath))
        {
            return string.Empty;
        }

        var relativePath = evidenceUrl.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_environment.WebRootPath, relativePath));
        var webRoot = Path.GetFullPath(_environment.WebRootPath);
        if (!fullPath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        var bytes = System.IO.File.ReadAllBytes(fullPath);
        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static EmployeeMessage BuildViolationNotificationMessage(User employee, ViolationRecord violation, string reviewer)
    {
        var evidenceLine = string.IsNullOrWhiteSpace(violation.EvidenceUrl)
            ? "Ảnh minh chứng: chưa có dữ liệu ảnh."
            : "Ảnh minh chứng: đã được mã hóa, chỉ xem trong tab Lịch sử vi phạm.";

        return new EmployeeMessage
        {
            EmployeeUserId = employee.Id,
            EmployeeUsername = employee.Username,
            EmployeeName = string.IsNullOrWhiteSpace(employee.FullName) ? employee.Username : employee.FullName,
            Channel = string.IsNullOrWhiteSpace(reviewer) ? "manager" : reviewer,
            SenderRole = "Manager",
            SenderName = reviewer,
            Title = $"Thông báo vi phạm: {violation.ViolationType}",
            Content =
                $"Mã vi phạm: {violation.TrackingId}\n" +
                $"Loại vi phạm: {violation.ViolationType}\n" +
                $"Mức độ: {violation.Severity}\n" +
                $"Camera: {VietnameseText.NormalizeMojibake(violation.CameraLocation)}\n" +
                $"Thời gian ghi nhận: {violation.DetectedAtUtc:dd/MM/yyyy HH:mm:ss}\n" +
                $"Trạng thái: {violation.Status}\n" +
                $"{evidenceLine}\n" +
                $"Ghi chú quản lý: {violation.ReviewNote ?? "Không có"}",
            SentAt = DateTime.UtcNow,
            IsRead = false
        };
    }

    private async Task NotifyViolationReviewedAsync(User employee, ViolationRecord violation)
    {
        var message = $"Bạn vừa nhận thông báo vi phạm {violation.TrackingId}. Mở Lịch sử vi phạm để xem nội dung và ảnh minh chứng.";
        var groups = new[]
        {
            InternalChatHub.BuildUsernameGroup(employee.Username),
            InternalChatHub.BuildUserIdGroup(employee.Id.ToString())
        };

        await _chatHub.Clients.Groups(groups).SendAsync("ReceiveNotification", message);
        await _chatHub.Clients.Groups(groups).SendAsync("MessagesChanged", new
        {
            employeeUserId = employee.Id,
            employeeUsername = employee.Username,
            channel = "violations",
            violationId = violation.Id,
            trackingId = violation.TrackingId
        });
    }
}

public class AddEmployeeRequest
{
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class ReviewComplaintRequest
{
    public Guid ViolationId { get; set; }
    public string? Decision { get; set; }   // "Accepted" | "Rejected"
    public string? ReviewNote { get; set; }
}
