using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Models;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin")]
public class AdminController : Controller
{
    private readonly IModelSettingService _modelSettingService;
    private readonly ViolationDbContext _context;
    private readonly ITelegramAlertService _telegramAlertService;
    private readonly IViolationMonitoringOrchestrator _monitoringOrchestrator;
    private readonly ViolationMonitoringOptions _monitoringOptions;
    private readonly TelegramBotOptions _telegramOptions;

    public AdminController(
        IModelSettingService modelSettingService,
        ViolationDbContext context,
        ITelegramAlertService telegramAlertService,
        IViolationMonitoringOrchestrator monitoringOrchestrator,
        IOptions<ViolationMonitoringOptions> monitoringOptions,
        IOptions<TelegramBotOptions> telegramOptions)
    {
        _modelSettingService = modelSettingService;
        _context = context;
        _telegramAlertService = telegramAlertService;
        _monitoringOrchestrator = monitoringOrchestrator;
        _monitoringOptions = monitoringOptions.Value;
        _telegramOptions = telegramOptions.Value;
    }

    private async Task WriteLogAsync(string action, string details, string status = "Thành công")
    {
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var username = User.Identity?.Name ?? "Admin System";
            var log = new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Username = username,
                Action = action,
                Details = details,
                IpAddress = ip,
                Status = status
            };
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Ignore logging failures to prevent disrupting the application
        }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeSetting = await _modelSettingService.GetActiveSettingAsync(cancellationToken);
        var managers = await _context.Users.Where(u => u.Role == "Manager").OrderByDescending(u => u.CreatedAtUtc).ToListAsync(cancellationToken);
        var aiModels = await _context.AiModels.OrderByDescending(m => m.CreatedAtUtc).ToListAsync(cancellationToken);

        // Calculate actual statistics from the database
        var totalEmployees = await _context.Users.CountAsync(u => u.Role == "Manager" || u.Role == "Employee", cancellationToken);
        
        var todayUtc = DateTime.UtcNow.Date;
        var violationsToday = await _context.ViolationRecords.CountAsync(v => v.DetectedAtUtc >= todayUtc, cancellationToken);
        
        var pendingRequests = await _context.ApprovalRequests.CountAsync(r => r.Status == "Chờ duyệt", cancellationToken);

        // Compliance rate: percentage of employees who did NOT violate today
        var usersWithViolationsToday = await _context.ViolationRecords
            .Where(v => v.DetectedAtUtc >= todayUtc)
            .Select(v => v.EmployeeCode)
            .Distinct()
            .CountAsync(cancellationToken);

        var complianceRate = totalEmployees > 0 
            ? (int)Math.Round((double)(totalEmployees - usersWithViolationsToday) / totalEmployees * 100) 
            : 100;
        complianceRate = Math.Max(0, Math.Min(100, complianceRate));

        // Get 5 most recent violations
        var recentViolations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        ViewBag.Managers = managers;
        ViewBag.AiModels = aiModels;
        ViewBag.TotalEmployees = totalEmployees;
        ViewBag.ViolationsToday = violationsToday;
        ViewBag.PendingRequests = pendingRequests;
        ViewBag.ComplianceRate = complianceRate;
        ViewBag.RecentViolations = recentViolations;

        return View("Index", activeSetting);
    }

    [HttpGet("Index")]
    public IActionResult KillIndex()
    {
        return Redirect("/Admin");
    }

    [HttpGet("Models")]
    public async Task<IActionResult> Models(CancellationToken cancellationToken)
    {
        var aiModels = await _context.AiModels.OrderByDescending(m => m.CreatedAtUtc).ToListAsync(cancellationToken);
        return View("Models", aiModels);
    }

    [HttpGet("Personnel")]
    public async Task<IActionResult> Personnel(CancellationToken cancellationToken)
    {
        var users = await _context.Users
            .Where(u => u.Role == "Manager")
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Compute real violation counts for each user based on EmployeeCode
        var violationCounts = await _context.ViolationRecords
            .GroupBy(v => v.EmployeeCode)
            .Select(g => new { EmployeeCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeCode, x => x.Count, cancellationToken);
            
        ViewBag.ViolationCounts = violationCounts;

        return View("Personnel", users);
    }

    [HttpGet("AuditLogs")]
    public async Task<IActionResult> AuditLogs(CancellationToken cancellationToken)
    {
        var logs = await _context.AuditLogs
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync(cancellationToken);
        return View("AuditLogs", logs);
    }

    [HttpGet("Settings")]
    public IActionResult Settings()
    {
        return View("Settings");
    }

    [HttpGet("Monitoring")]
    public async Task<IActionResult> Monitoring(CancellationToken cancellationToken)
    {
        var updates = await _telegramAlertService.GetRecentUpdatesAsync(cancellationToken);
        var knownChatIds = _telegramAlertService.GetKnownChatIds();
        var recentViolations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        var vm = new MonitoringCenterViewModel
        {
            PollingIntervalSeconds = _monitoringOptions.PollingIntervalSeconds,
            SmokeDetectionThresholdCount = _monitoringOptions.SmokeDetectionThresholdCount,
            EmptyChairThresholdMinutes = _monitoringOptions.EmptyChairThresholdMinutes,
            CameraLocation = _monitoringOptions.CameraLocation,
            TelegramEnabled = _telegramOptions.Enabled,
            ConfiguredChatIds = string.Join(", ", _telegramOptions.ChatIds),
            KnownChatIds = string.Join(", ", knownChatIds),
            RecentTelegramUpdates = updates,
            RecentViolations = recentViolations,
            LastAlertResult = TempData.ContainsKey("MonitoringAlertJson")
                ? System.Text.Json.JsonSerializer.Deserialize<ViolationAlertResult>(TempData["MonitoringAlertJson"]?.ToString() ?? string.Empty)
                : null,
            LastTelegramSendResult = TempData.ContainsKey("TelegramSendJson")
                ? System.Text.Json.JsonSerializer.Deserialize<TelegramSendResult>(TempData["TelegramSendJson"]?.ToString() ?? string.Empty)
                : null
        };

        ViewData["ActivePage"] = "Monitoring";
        ViewData["Title"] = "Monitoring Center";
        return View("Monitoring", vm);
    }

    [HttpGet("ProfileSettings")]
    public IActionResult ProfileSettings()
    {
        return View("ProfileSettings");
    }

    [HttpGet("GeneralSettings")]
    public IActionResult GeneralSettings()
    {
        return View("GeneralSettings");
    }

    private string GetRedirectUrl()
    {
        string referer = Request.Headers["Referer"].ToString();
        return string.IsNullOrEmpty(referer) ? "/Admin" : referer;
    }

    [HttpPost("AddPersonnel")]
    public async Task<IActionResult> AddPersonnel(
        string fullName,
        string username,
        string password,
        string role,
        string department,
        string email,
        string phone,
        string employeeCode,
        CancellationToken cancellationToken)
    {
        role = "Manager";
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin bắt buộc.";
            return RedirectToAction("Personnel");
        }

        var existingUser = await _context.Users.AnyAsync(u => u.Username == username, cancellationToken);
        if (existingUser)
        {
            TempData["ErrorMessage"] = "Tên đăng nhập đã tồn tại.";
            return RedirectToAction("Personnel");
        }

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = fullName,
            PasswordHash = PasswordHasher.HashPassword(password),
            Role = role,
            Department = department ?? string.Empty,
            Email = email ?? string.Empty,
            Phone = phone ?? string.Empty,
            EmployeeCode = employeeCode ?? string.Empty,
            FaceImagePath = "",
            ManagerKey = role.Equals("Manager", StringComparison.OrdinalIgnoreCase) ? "hieudeptraivcl" : string.Empty,
            IsKeyActivated = !role.Equals("Manager", StringComparison.OrdinalIgnoreCase),
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Thêm Nhân sự", $"Tạo tài khoản {username} với vai trò {role}", "Thành công");

        TempData["SuccessMessage"] = $"Đã thêm nhân viên {fullName} thành công!";
        return RedirectToAction("Personnel");
    }

    [HttpPost("EditPersonnel")]
    public async Task<IActionResult> EditPersonnel(
        Guid id,
        string fullName,
        string username,
        string password,
        string department,
        string email,
        string phone,
        string employeeCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin bắt buộc.";
            return RedirectToAction("Personnel");
        }

        var user = await _context.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy tài khoản quản lý.";
            return RedirectToAction("Personnel");
        }

        if (user.Username != username)
        {
            var existingUser = await _context.Users.AnyAsync(u => u.Username == username, cancellationToken);
            if (existingUser)
            {
                TempData["ErrorMessage"] = "Tên đăng nhập đã tồn tại.";
                return RedirectToAction("Personnel");
            }
        }

        user.FullName = fullName;
        user.Username = username;
        user.Department = department ?? string.Empty;
        user.Email = email ?? string.Empty;
        user.Phone = phone ?? string.Empty;
        user.EmployeeCode = employeeCode ?? string.Empty;

        if (!string.IsNullOrEmpty(password))
        {
            user.PasswordHash = PasswordHasher.HashPassword(password);
        }

        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Cập nhật Nhân sự", $"Cập nhật tài khoản quản lý {username}", "Thành công");

        TempData["SuccessMessage"] = $"Đã cập nhật tài khoản quản lý {fullName} thành công!";
        return RedirectToAction("Personnel");
    }

    [HttpPost("DeletePersonnel")]
    public async Task<IActionResult> DeletePersonnel(Guid id, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy tài khoản nhân sự.";
            return RedirectToAction("Personnel");
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Xóa Nhân sự", $"Đã xóa tài khoản {user.Username}", "Thành công");

        TempData["SuccessMessage"] = $"Đã xóa tài khoản {user.FullName} thành công!";
        return RedirectToAction("Personnel");
    }

    [HttpPost("UpdateModelSettings")]
    public async Task<IActionResult> UpdateModelSettings(string yoloModelPath, decimal yoloConfThreshold, decimal yoloIouThreshold, decimal deepfaceConfThreshold, CancellationToken cancellationToken)
    {
        var setting = new ModelSetting
        {
            YoloModelPath = yoloModelPath,
            YoloConfThreshold = yoloConfThreshold,
            YoloIouThreshold = yoloIouThreshold,
            DeepfaceConfThreshold = deepfaceConfThreshold
        };
        await _modelSettingService.UpdateSettingAsync(setting, cancellationToken);

        await WriteLogAsync("Cập nhật cấu hình", "Cập nhật thông số mô hình AI chung");

        TempData["SuccessMessage"] = "Cập nhật cấu hình AI thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("UpdateManagerKey")]
    public async Task<IActionResult> UpdateManagerKey(Guid managerId, string newKey, CancellationToken cancellationToken)
    {
        var manager = await _context.Users.FindAsync(new object[] { managerId }, cancellationToken);
        if (manager != null && manager.Role == "Manager")
        {
            manager.ManagerKey = newKey;
            await _context.SaveChangesAsync(cancellationToken);

            await WriteLogAsync("Cập nhật Key Manager", $"Cập nhật mã kích hoạt cho {manager.Username}");

            TempData["SuccessMessage"] = $"Đã cập nhật khóa cho {manager.FullName}!";
        }
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("ResetManagerActivation")]
    public async Task<IActionResult> ResetManagerActivation(Guid managerId, CancellationToken cancellationToken)
    {
        var manager = await _context.Users.FindAsync(new object[] { managerId }, cancellationToken);
        if (manager != null && manager.Role == "Manager")
        {
            manager.IsKeyActivated = false;
            await _context.SaveChangesAsync(cancellationToken);

            await WriteLogAsync("Reset thiết bị", $"Gỡ kích hoạt thiết bị đối với {manager.Username}");

            TempData["SuccessMessage"] = $"Đã reset thiết bị cho {manager.FullName}!";
        }
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("AddAiModel")]
    public async Task<IActionResult> AddAiModel(string name, string type, string modelPath, decimal confThreshold, decimal iouThreshold, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(modelPath))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin mô hình.";
            return Redirect(GetRedirectUrl());
        }

        var newModel = new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            ModelPath = modelPath,
            ConfThreshold = confThreshold,
            IouThreshold = iouThreshold,
            IsActive = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.AiModels.Add(newModel);
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Thêm Model AI", $"Đã thêm mô hình {name} ({type})");

        TempData["SuccessMessage"] = $"Đã thêm mô hình {name} thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("EditAiModel")]
    public async Task<IActionResult> EditAiModel(Guid id, string name, string modelPath, decimal confThreshold, decimal iouThreshold, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null) return Redirect(GetRedirectUrl());

        model.Name = name;
        model.ModelPath = modelPath;
        model.ConfThreshold = confThreshold;
        model.IouThreshold = iouThreshold;
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Sửa Model AI", $"Cập nhật thông số mô hình {name}");

        TempData["SuccessMessage"] = $"Đã cập nhật mô hình {name} thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("ToggleAiModel")]
    public async Task<IActionResult> ToggleAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null) return Redirect(GetRedirectUrl());

        if (!model.IsActive)
        {
            var otherActiveModels = await _context.AiModels.Where(m => m.Type == model.Type && m.IsActive && m.Id != model.Id).ToListAsync(cancellationToken);
            foreach (var other in otherActiveModels) other.IsActive = false;
            model.IsActive = true;
            TempData["SuccessMessage"] = $"Đã kích hoạt mô hình {model.Name}!";
        }
        else
        {
            model.IsActive = false;
            TempData["SuccessMessage"] = $"Đã tắt mô hình {model.Name}!";
        }
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Bật/Tắt Model AI", $"Thay đổi kích hoạt mô hình {model.Name} sang {(model.IsActive ? "Bật" : "Tắt")}");

        return Redirect(GetRedirectUrl());
    }

    [HttpPost("DeleteAiModel")]
    public async Task<IActionResult> DeleteAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null || model.IsActive) return Redirect(GetRedirectUrl());

        _context.AiModels.Remove(model);
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Xóa Model AI", $"Đã xóa mô hình {model.Name}");

        TempData["SuccessMessage"] = $"Đã xóa mô hình {model.Name}!";
        return Redirect(GetRedirectUrl());
    }
    [HttpPost("Monitoring/TestSmoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmoke(CancellationToken cancellationToken)
    {
        var result = await _monitoringOrchestrator.TriggerSmokeTestAsync(cancellationToken);
        TempData["MonitoringAlertJson"] = System.Text.Json.JsonSerializer.Serialize(result);
        await WriteLogAsync("Test Monitoring", "Đã chạy testcase hút thuốc thủ công", "Cảnh báo");
        TempData["SuccessMessage"] = "Đã kích hoạt testcase hút thuốc.";
        return RedirectToAction("Monitoring");
    }

    [HttpPost("Monitoring/TestLeaving")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestLeaving(CancellationToken cancellationToken)
    {
        var result = await _monitoringOrchestrator.TriggerLeavingPositionTestAsync(cancellationToken);
        TempData["MonitoringAlertJson"] = System.Text.Json.JsonSerializer.Serialize(result);
        await WriteLogAsync("Test Monitoring", "Đã chạy testcase rời vị trí thủ công", "Cảnh báo");
        TempData["SuccessMessage"] = "Đã kích hoạt testcase rời vị trí.";
        return RedirectToAction("Monitoring");
    }

    [HttpPost("Monitoring/SendTelegramTest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTelegramTest(string chatId, string message, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(message)
            ? $"[TEST TELEGRAM] Monitoring Center gửi thử lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}."
            : message;

        var result = await _telegramAlertService.SendTestMessageAsync(text, chatId, cancellationToken);
        TempData["TelegramSendJson"] = System.Text.Json.JsonSerializer.Serialize(result);
        await WriteLogAsync("Test Telegram", $"Gửi tin nhắn thử tới {result.ChatId}", result.Success ? "Thành công" : "Lỗi");
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Success
            ? $"Đã gửi test Telegram tới chat {result.ChatId}."
            : $"Gửi test Telegram thất bại: {result.ResponseSummary}";
        return RedirectToAction("Monitoring");
    }
}
