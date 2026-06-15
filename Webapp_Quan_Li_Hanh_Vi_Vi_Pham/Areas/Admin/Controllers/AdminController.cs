using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
public class AdminController : Controller
{
    private readonly IModelSettingService _modelSettingService;
    private readonly ViolationDbContext _context;
    private readonly ITelegramAlertService _telegramAlertService;
    private readonly IViolationMonitoringOrchestrator _monitoringOrchestrator;
    private readonly IAiModelCatalogService _aiModelCatalogService;
    private readonly ViolationMonitoringOptions _monitoringOptions;
    private readonly TelegramBotOptions _telegramOptions;

    public AdminController(
        IModelSettingService modelSettingService,
        ViolationDbContext context,
        ITelegramAlertService telegramAlertService,
        IViolationMonitoringOrchestrator monitoringOrchestrator,
        IAiModelCatalogService aiModelCatalogService,
        IOptions<ViolationMonitoringOptions> monitoringOptions,
        IOptions<TelegramBotOptions> telegramOptions)
    {
        _modelSettingService = modelSettingService;
        _context = context;
        _telegramAlertService = telegramAlertService;
        _monitoringOrchestrator = monitoringOrchestrator;
        _aiModelCatalogService = aiModelCatalogService;
        _monitoringOptions = monitoringOptions.Value;
        _telegramOptions = telegramOptions.Value;
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return null;
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    [HttpGet("/Admin/GetNotifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var admin = await GetCurrentUserAsync(cancellationToken);
        if (admin == null) return Json(new { success = false, message = "Không xác định được tài khoản quản lý." });

        // Messages SENT TO admin
        var messages = await _context.EmployeeMessages
            .Where(m => m.Channel == admin.Username && m.SenderRole == "Employee")
            .OrderByDescending(m => m.SentAt)
            .Take(10)
            .Select(m => new
            {
                source = "message",
                id = m.Id,
                title = string.IsNullOrWhiteSpace(m.Title) ? $"Tin nhắn từ {m.SenderName}" : m.Title,
                body = m.Content,
                createdAt = m.SentAt,
                isRead = m.IsRead,
                tab = "messages"
            })
            .ToListAsync(cancellationToken);

        // Approval Requests for Admin
        var requestUpdates = await _context.ApprovalRequests
            .Where(r => r.Status == "Chờ duyệt" || r.Status == "Pending")
            .OrderByDescending(r => r.SubmittedAt)
            .Take(10)
            .Select(r => new
            {
                source = "request",
                id = r.Id,
                title = $"Đơn xin {r.RequestType} mới",
                body = $"Từ: {r.EmployeeName}",
                createdAt = r.SubmittedAt,
                isRead = false,
                tab = "requests"
            })
            .ToListAsync(cancellationToken);

        var combined = messages
            .Concat(requestUpdates)
            .OrderByDescending(item => item.createdAt)
            .Take(12)
            .ToList();

        return Json(new
        {
            success = true,
            data = combined,
            unreadCount = combined.Count(m => !m.isRead)
        });
    }

    [HttpPost("/Admin/MarkNotificationRead")]
    public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken cancellationToken)
    {
        var admin = await GetCurrentUserAsync(cancellationToken);
        if (admin == null) return Json(new { success = false });

        var msg = await _context.EmployeeMessages.FindAsync(new object[] { id }, cancellationToken);
        if (msg != null && msg.Channel == admin.Username)
        {
            msg.IsRead = true;
            await _context.SaveChangesAsync(cancellationToken);
        }
        return Json(new { success = true });
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

    [HttpGet("/Admin")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeSetting = await _modelSettingService.GetActiveSettingAsync(cancellationToken);
        var managers = await _context.Users.Where(u => u.Role == "Manager").OrderByDescending(u => u.CreatedAtUtc).ToListAsync(cancellationToken);
        var aiModels = await _context.AiModels.OrderByDescending(m => m.CreatedAtUtc).ToListAsync(cancellationToken);
        await NormalizePersistedAiModelThresholdsAsync(aiModels, cancellationToken);

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
        ViewData["ActivePage"] = "Dashboard";
        return View(activeSetting);
    }

    [HttpGet("/Admin/Models")]
    public async Task<IActionResult> Models(CancellationToken cancellationToken)
    {
        var models = await _context.AiModels
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        await NormalizePersistedAiModelThresholdsAsync(models, cancellationToken);
        var viewModel = await _aiModelCatalogService.BuildPageViewModelAsync(models, cancellationToken);
        return View(viewModel);
    }

    [HttpGet("/Admin/Personnel")]
    public async Task<IActionResult> Personnel(CancellationToken cancellationToken)
    {
        var managers = await _context.Users
            .Where(u => u.Role == "Manager")
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        ViewBag.ViolationCounts = await _context.ViolationRecords
            .Where(v => !string.IsNullOrWhiteSpace(v.EmployeeCode))
            .GroupBy(v => v.EmployeeCode)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);

        return View(managers);
    }

    [HttpGet("/Admin/AuditLogs")]
    public async Task<IActionResult> AuditLogs(CancellationToken cancellationToken)
    {
        var logs = await _context.AuditLogs
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync(cancellationToken);
        return View(logs);
    }

    [HttpGet("/Admin/Monitoring")]
    public async Task<IActionResult> Monitoring(CancellationToken cancellationToken)
    {
        return View(await BuildMonitoringViewModelAsync(cancellationToken));
    }

    [HttpGet("/Admin/Settings")]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        ViewData["ActivePage"] = "SettingsSystem";
        return View(await _modelSettingService.GetActiveSettingAsync(cancellationToken));
    }

    [HttpPost("/Admin/Settings/DeepFace")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDeepFaceSettings(
        string? deepfaceDetectorBackend,
        bool deepfaceAlign,
        bool deepfaceEnforceDetection,
        CancellationToken cancellationToken)
    {
        var active = await _modelSettingService.GetActiveSettingAsync(cancellationToken);
        var updated = new ModelSetting
        {
            YoloModelPath = active.YoloModelPath,
            YoloConfThreshold = active.YoloConfThreshold,
            YoloIouThreshold = active.YoloIouThreshold,
            DeepfaceConfThreshold = active.DeepfaceConfThreshold,
            DeepfaceDetectorBackend = string.IsNullOrWhiteSpace(deepfaceDetectorBackend) ? active.DeepfaceDetectorBackend : deepfaceDetectorBackend,
            DeepfaceAlign = deepfaceAlign,
            DeepfaceEnforceDetection = deepfaceEnforceDetection,
            IsActive = active.IsActive
        };

        await _modelSettingService.UpdateSettingAsync(updated, cancellationToken);
        TempData["SuccessMessage"] = "Đã cập nhật cấu hình DeepFace cho đăng ký, đăng nhập và cập nhật khuôn mặt.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet("/Admin/ProfileSettings")]
    public IActionResult ProfileSettings()
    {
        return View();
    }

    [HttpGet("/Admin/GeneralSettings")]
    public IActionResult GeneralSettings()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmoke(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _monitoringOrchestrator.TriggerSmokeTestAsync(cancellationToken);
            TempData["SuccessMessage"] = $"Da chay smoke testcase. Track: {result.TrackId}, Severity: {result.Severity}.";
            TempData["LastAlertResult"] = JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Smoke testcase that bai: {ex.Message}";
        }

        return RedirectToAction(nameof(Monitoring));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestLeaving(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _monitoringOrchestrator.TriggerLeavingPositionTestAsync(cancellationToken);
            TempData["SuccessMessage"] = $"Da chay leaving testcase. Track: {result.TrackId}, Severity: {result.Severity}.";
            TempData["LastAlertResult"] = JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Leaving testcase that bai: {ex.Message}";
        }

        return RedirectToAction(nameof(Monitoring));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTelegramTest(string? chatId, string? message, CancellationToken cancellationToken)
    {
        try
        {
            var payload = string.IsNullOrWhiteSpace(message)
                ? "[TEST TELEGRAM] Gui tu Monitoring Center"
                : message;

            var result = await _telegramAlertService.SendTestMessageAsync(payload, chatId, cancellationToken);
            TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.ResponseSummary;
            TempData["LastTelegramSendResult"] = JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Gui Telegram test that bai: {ex.Message}";
        }

        return RedirectToAction(nameof(Monitoring));
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
            YoloConfThreshold = NormalizeYoloThreshold(yoloConfThreshold, 0.25m),
            YoloIouThreshold = NormalizeYoloThreshold(yoloIouThreshold, 0.45m),
            DeepfaceConfThreshold = NormalizeThresholdRange(deepfaceConfThreshold, 0.40m)
        };
        await _modelSettingService.UpdateSettingAsync(setting, cancellationToken);
        TempData["SuccessMessage"] = "Cập nhật cấu hình mô hình AI thành công!";
        return RedirectToAction("Index");
    }

    [HttpPost]
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
        else
        {
            TempData["ErrorMessage"] = "Không tìm thấy tài khoản quản lý.";
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
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
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> AddAiModel(string name, string type, string modelPath, decimal confThreshold, decimal iouThreshold, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(modelPath))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin mô hình.";
            return RedirectToAction(nameof(Models));
        }

        // Chuẩn hóa về thang 0-1: nếu user nhập > 1.0 (thang 100) thì chia 100
        var isYoloModel = IsYoloModel(type, modelPath);
        var normalizedConf = isYoloModel
            ? NormalizeYoloThreshold(confThreshold, 0.25m)
            : NormalizeThresholdRange(confThreshold, 0.40m);
        var normalizedIou = isYoloModel
            ? NormalizeYoloThreshold(iouThreshold, 0.45m)
            : 0m;

        var newModel = new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            ModelPath = modelPath,
            ConfThreshold = normalizedConf,
            IouThreshold = normalizedIou,
            IsActive = false, // starts as inactive, user can toggle active
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.AiModels.Add(newModel);
        await _context.SaveChangesAsync(cancellationToken);

        await WriteLogAsync("Thêm Model AI", $"Đã thêm mô hình {name} ({type})");

        TempData["SuccessMessage"] = $"Đã thêm mô hình {name} thành công!";
        return RedirectToAction(nameof(Models));
    }

    [HttpPost]
    public async Task<IActionResult> EditAiModel(
        Guid id, 
        string name, 
        string modelPath, 
        decimal confThreshold, 
        decimal iouThreshold, 
        CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction(nameof(Models));
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(modelPath))
        {
            TempData["ErrorMessage"] = "Vui lòng điền đầy đủ tên và đường dẫn mô hình.";
            return RedirectToAction(nameof(Models));
        }

        // Chuẩn hóa về thang 0-1: nếu user nhập > 1.0 (thang 100) thì chia 100
        var isYoloModel = IsYoloModel(model.Type, modelPath);
        var normalizedConf = isYoloModel
            ? NormalizeYoloThreshold(confThreshold, 0.25m)
            : NormalizeThresholdRange(confThreshold, 0.40m);
        var normalizedIou = isYoloModel
            ? NormalizeYoloThreshold(iouThreshold, 0.45m)
            : 0m;

        model.Name = name;
        model.ModelPath = modelPath;
        model.ConfThreshold = normalizedConf;
        model.IouThreshold = normalizedIou;
        await _context.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = $"Đã cập nhật thông số mô hình {name} thành công!";
        return RedirectToAction(nameof(Models));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction(nameof(Models));
        }

        // Toggle active status
        if (!model.IsActive)
        {
            // Deactivate other models of the SAME type
            var otherActiveModels = await _context.AiModels
                .Where(m => m.Type == model.Type && m.IsActive && m.Id != model.Id)
                .ToListAsync(cancellationToken);

            foreach (var other in otherActiveModels)
            {
                other.IsActive = false;
            }

            model.IsActive = true;
            TempData["SuccessMessage"] = $"Đã kích hoạt mô hình {model.Name}!";
        }
        else
        {
            model.IsActive = false;
            TempData["SuccessMessage"] = $"Đã tắt mô hình {model.Name}!";
        }
        await _context.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Models));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction(nameof(Models));
        }

        if (model.IsActive)
        {
            TempData["ErrorMessage"] = "Không thể xóa mô hình đang ở trạng thái kích hoạt.";
            return RedirectToAction(nameof(Models));
        }

        _context.AiModels.Remove(model);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = $"Đã xóa mô hình {model.Name} thành công!";
        return RedirectToAction(nameof(Models));
    }

    private async Task NormalizePersistedAiModelThresholdsAsync(List<AiModel> models, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var model in models)
        {
            var isYoloModel = IsYoloModel(model.Type, model.ModelPath);
            var normalizedConf = isYoloModel
                ? NormalizeYoloThreshold(model.ConfThreshold, 0.25m)
                : NormalizeThresholdRange(model.ConfThreshold, 0.40m);
            var normalizedIou = isYoloModel
                ? NormalizeYoloThreshold(model.IouThreshold, 0.45m)
                : 0m;

            if (model.ConfThreshold == normalizedConf && model.IouThreshold == normalizedIou)
            {
                continue;
            }

            model.ConfThreshold = normalizedConf;
            model.IouThreshold = normalizedIou;
            changed = true;
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsYoloModel(string? type, string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(type)
            && type.StartsWith("Yolo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(modelPath ?? string.Empty);
        return extension.Equals(".pt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal NormalizeYoloThreshold(decimal value, decimal fallback)
    {
        return NormalizeThresholdRange(value, fallback, min: 0.05m, max: 1.00m);
    }

    private static decimal NormalizeThresholdRange(decimal value, decimal fallback, decimal min = 0.05m, decimal max = 1.00m)
    {
        var normalized = value;
        if (normalized <= 0m)
        {
            normalized = fallback;
        }
        else if (normalized > 1m)
        {
            normalized /= 100m;
        }

        normalized = Math.Clamp(normalized, min, max);
        return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private async Task<MonitoringCenterViewModel> BuildMonitoringViewModelAsync(CancellationToken cancellationToken)
    {
        var updates = await _telegramAlertService.GetRecentUpdatesAsync(cancellationToken);
        var recentViolations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        ViolationAlertResult? lastAlertResult = null;
        TelegramSendResult? lastTelegramSendResult = null;

        if (TempData.TryGetValue("LastAlertResult", out var alertObj) && alertObj is string alertJson && !string.IsNullOrWhiteSpace(alertJson))
        {
            lastAlertResult = JsonSerializer.Deserialize<ViolationAlertResult>(alertJson);
        }

        if (TempData.TryGetValue("LastTelegramSendResult", out var telegramObj) && telegramObj is string telegramJson && !string.IsNullOrWhiteSpace(telegramJson))
        {
            lastTelegramSendResult = JsonSerializer.Deserialize<TelegramSendResult>(telegramJson);
        }

        return new MonitoringCenterViewModel
        {
            PollingIntervalSeconds = _monitoringOptions.PollingIntervalSeconds,
            SmokeDetectionThresholdCount = _monitoringOptions.SmokeDetectionThresholdCount,
            EmptyChairThresholdMinutes = _monitoringOptions.EmptyChairThresholdMinutes,
            CameraLocation = _monitoringOptions.CameraLocation,
            TelegramEnabled = _telegramOptions.Enabled,
            ConfiguredChatIds = string.Join(", ", _telegramOptions.ChatIds ?? []),
            KnownChatIds = string.Join(", ", _telegramAlertService.GetKnownChatIds()),
            RecentTelegramUpdates = updates,
            RecentViolations = recentViolations,
            LastAlertResult = lastAlertResult,
            LastTelegramSendResult = lastTelegramSendResult
        };
    }
}
