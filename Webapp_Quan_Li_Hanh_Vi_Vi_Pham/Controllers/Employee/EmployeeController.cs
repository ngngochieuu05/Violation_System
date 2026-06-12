using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers.Employee;

[Authorize(Roles = "Employee")]
public class EmployeeController : Controller
{
    private readonly ViolationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<InternalChatHub> _chatHub;

    public EmployeeController(ViolationDbContext context, IWebHostEnvironment environment, IHubContext<InternalChatHub> chatHub)
    {
        _context = context;
        _environment = environment;
        _chatHub = chatHub;
    }

    public IActionResult Index() => View();

    public IActionResult Attendance() => RedirectToAction(nameof(Index), new { tab = "attendance" });
    public IActionResult Messages() => RedirectToAction(nameof(Index), new { tab = "messages" });
    public IActionResult Requests() => RedirectToAction(nameof(Index), new { tab = "requests" });
    public IActionResult Profile() => RedirectToAction(nameof(Index), new { tab = "profile" });
    public IActionResult Settings() => RedirectToAction(nameof(Index), new { tab = "settings" });
    public IActionResult Payroll() => RedirectToAction(nameof(Index), new { tab = "payroll" });
    public IActionResult KnowledgeBase() => RedirectToAction(nameof(Index), new { tab = "knowledge" });
    public IActionResult Violations() => RedirectToAction(nameof(Index), new { tab = "violations" });

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        return Json(new
        {
            success = true,
            data = new
            {
                user.Id,
                user.Username,
                user.FullName,
                user.Email,
                user.Phone,
                user.Department,
                user.EmployeeCode,
                user.Role,
                hasPayrollPin = !string.IsNullOrEmpty(user.PayrollPin),
                avatarUrl = string.IsNullOrWhiteSpace(user.AvatarPath)
                    ? null
                    : $"{user.AvatarPath}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        user.FullName = request.Name?.Trim() ?? user.FullName;
        user.Department = request.Department?.Trim() ?? user.Department;
        user.Email = request.Email?.Trim() ?? user.Email;
        user.Phone = request.Phone?.Trim() ?? user.Phone;

        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true, message = "Da cap nhat ho so." });
    }

    [HttpPost]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadAvatar(IFormFile? avatar, string? avatarBase64, string? fileName, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".jfif" };
        byte[] imageBytes;
        string extension;

        if (avatar != null && avatar.Length > 0)
        {
            extension = Path.GetExtension(avatar.FileName);
            if (!allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Dinh dang anh khong duoc ho tro." });
            }

            await using var memory = new MemoryStream();
            await avatar.CopyToAsync(memory, cancellationToken);
            imageBytes = memory.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(avatarBase64))
        {
            extension = Path.GetExtension(fileName ?? ".jpg");
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            if (!allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                extension = ".jpg";
            }

            var commaIndex = avatarBase64.IndexOf(',');
            var payload = commaIndex >= 0 ? avatarBase64[(commaIndex + 1)..] : avatarBase64;
            try
            {
                imageBytes = Convert.FromBase64String(payload);
            }
            catch
            {
                return Json(new { success = false, message = "Anh dai dien khong hop le." });
            }
        }
        else
        {
            return Json(new { success = false, message = "Vui long chon anh hop le." });
        }

        if (imageBytes.Length == 0)
        {
            return Json(new { success = false, message = "Anh dai dien rong." });
        }

        if (imageBytes.Length > 8 * 1024 * 1024)
        {
            return Json(new { success = false, message = "Anh dai dien qua lon." });
        }

        if (!allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "Dinh dang anh khong duoc ho tro." });
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(uploadsRoot);

        var savedFileName = $"{user.Id:N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(uploadsRoot, savedFileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);

        user.AvatarPath = $"/uploads/avatars/{savedFileName}";
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = true,
            avatarUrl = $"{user.AvatarPath}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
        });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAvatar(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        user.AvatarPath = string.Empty;
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var preference = await _context.EmployeePreferences
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (preference == null)
        {
            preference = new EmployeePreference
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.EmployeePreferences.Add(preference);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Json(new
        {
            success = true,
            data = new
            {
                notifications = preference.NotificationsEnabled,
                compact = preference.CompactMode,
                reducedMotion = preference.ReducedMotion,
                language = preference.Language,
                theme = preference.Theme
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var preference = await _context.EmployeePreferences
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        if (preference == null)
        {
            preference = new EmployeePreference
            {
                Id = Guid.NewGuid(),
                UserId = user.Id
            };
            _context.EmployeePreferences.Add(preference);
        }

        preference.NotificationsEnabled = request.Notifications;
        preference.CompactMode = request.Compact;
        preference.ReducedMotion = request.ReducedMotion;
        preference.Language = string.IsNullOrWhiteSpace(request.Language) ? "vi-VN" : request.Language;
        preference.Theme = string.IsNullOrWhiteSpace(request.Theme) ? "light" : request.Theme;
        preference.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Da cap nhat thiet lap." });
    }

    [HttpGet]
    public async Task<IActionResult> GetAttendance(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var sessions = await _context.WorkSessions
            .Where(w => w.EmployeeUserId == user.Id)
            .OrderByDescending(w => w.CheckInTime)
            .Take(30)
            .ToListAsync(cancellationToken);

        var current = sessions.FirstOrDefault(s => s.CheckOutTime == null);
        var completed = sessions.Where(s => s.CheckOutTime != null)
            .OrderByDescending(s => s.CheckInTime)
            .Select(MapSession)
            .ToList();

        return Json(new
        {
            success = true,
            data = new
            {
                currentSession = current == null ? null : MapSession(current),
                sessions = completed
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> CheckIn([FromBody] AttendanceCaptureRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var activeSession = await _context.WorkSessions
            .FirstOrDefaultAsync(w => w.EmployeeUserId == user.Id && w.CheckOutTime == null, cancellationToken);

        if (activeSession != null)
        {
            return Json(new { success = false, message = "Ban da check-in roi." });
        }

        var now = DateTime.UtcNow;
        var imagePath = await SaveAttendanceImageAsync(user.Id, request.ImageDataUrl, "checkin", cancellationToken);
        var workSession = new WorkSession
        {
            EmployeeUserId = user.Id,
            EmployeeId = user.Id.ToString(),
            EmployeeCode = user.EmployeeCode,
            EmployeeName = user.FullName,
            Date = now.Date,
            CheckInTime = now,
            CheckOutTime = null,
            Status = now.TimeOfDay > new TimeSpan(8, 15, 0) ? "Late" : "OnTime",
            Notes = string.Empty,
            CheckInImagePath = imagePath,
            CheckOutImagePath = string.Empty
        };

        _context.WorkSessions.Add(workSession);
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> CheckOut([FromBody] AttendanceCaptureRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var activeSession = await _context.WorkSessions
            .FirstOrDefaultAsync(w => w.EmployeeUserId == user.Id && w.CheckOutTime == null, cancellationToken);

        if (activeSession == null)
        {
            return Json(new { success = false, message = "Ban can check-in truoc khi check-out." });
        }

        var now = DateTime.UtcNow;
        activeSession.CheckOutTime = now;
        activeSession.CheckOutImagePath = await SaveAttendanceImageAsync(user.Id, request.ImageDataUrl, "checkout", cancellationToken);
        activeSession.Status = activeSession.CheckInTime.TimeOfDay > new TimeSpan(8, 15, 0) ? "Late" : "Completed";

        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetRequestTemplates(CancellationToken cancellationToken)
    {
        var templates = await _context.FormTemplates
            .OrderBy(f => f.Title)
            .Select(f => new
            {
                f.Id,
                f.Title,
                f.Description,
                f.FileUrl,
                f.LastUpdated
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = templates });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyViolations(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Nguoi dung khong hop le." });
        }

        var violations = await _context.ViolationRecords
            .Where(v => v.EmployeeCode == user.EmployeeCode || v.EmployeeCode == user.Username)
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = violations });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyTasks(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc danh tinh." });
        }

        var tasks = await _context.EmployeeTasks
            .Where(t => t.EmployeeId == user.Id)
            .OrderBy(t => t.DueDate)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = tasks });
    }

    [HttpPost]
    public async Task<IActionResult> AddTask([FromBody] EmployeeTask dto, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Không xác định được danh tính." });
        }

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return Json(new { success = false, message = "Tiêu đề công việc không được để trống." });
        }

        var task = new EmployeeTask
        {
            Id = Guid.NewGuid(),
            EmployeeId = user.Id,
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim() ?? string.Empty,
            DueDate = dto.DueDate,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.EmployeeTasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkTaskDone(Guid id, CancellationToken cancellationToken)
    {
        var task = await _context.EmployeeTasks.FindAsync(new object[] { id }, cancellationToken);
        if (task == null)
        {
            return Json(new { success = false, message = "Khong tim thay nhiem vu." });
        }

        task.Status = "Done";
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyPayrolls(int year, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc danh tinh." });
        }

        var payrolls = await _context.PayrollRecords
            .Where(p => p.EmployeeId == user.Id && p.Year == year)
            .OrderByDescending(p => p.Month)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = payrolls });
    }

    [HttpPost]
    public async Task<IActionResult> SendRequest([FromBody] SendApprovalRequestRequest dto, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        if (dto.TemplateId <= 0)
        {
            return Json(new { success = false, message = "Vui lòng chọn mẫu đơn hợp lệ." });
        }

        var template = await _context.FormTemplates
            .FirstOrDefaultAsync(f => f.Id == dto.TemplateId, cancellationToken);

        if (template == null)
        {
            return Json(new { success = false, message = "Không tìm thấy mẫu đơn trong hệ thống." });
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return Json(new { success = false, message = "Vui lòng nhập lý do gửi đơn." });
        }

        var contentParts = new List<string>
        {
            $"Mẫu đơn: {template.Title}"
        };

        if (!string.IsNullOrWhiteSpace(dto.RequestedDate))
        {
            contentParts.Add($"Ngày áp dụng: {dto.RequestedDate}");
        }

        contentParts.Add($"Lý do: {dto.Reason.Trim()}");

        if (!string.IsNullOrWhiteSpace(template.FileUrl))
        {
            contentParts.Add($"Tài liệu đính kèm: {template.FileUrl}");
        }

        var request = new ApprovalRequest
        {
            EmployeeUserId = user.Id,
            EmployeeUsername = user.Username,
            EmployeeName = user.FullName,
            RequestType = template.Title,
            Content = string.Join(Environment.NewLine, contentParts),
            SubmittedAt = DateTime.UtcNow,
            Status = "Chờ duyệt"
        };

        _context.ApprovalRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyRequests(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var requests = await _context.ApprovalRequests
            .Where(r => r.EmployeeUserId == user.Id)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = requests });
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest dto, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var message = new EmployeeMessage
        {
            EmployeeUserId = user.Id,
            EmployeeUsername = user.Username,
            EmployeeName = user.FullName,
            Channel = string.IsNullOrWhiteSpace(dto.Channel) ? "manager" : dto.Channel,
            SenderRole = "Employee",
            SenderName = user.FullName,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? "Tin nhắn mới" : dto.Title,
            Content = dto.Content ?? string.Empty,
            SentAt = DateTime.UtcNow,
            IsRead = false
        };

        _context.EmployeeMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        await NotifyConversationChangedAsync(user, message.Channel);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> EditMessage([FromBody] EditMessageRequest dto, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        if (dto.Id <= 0)
        {
            return Json(new { success = false, message = "Tin nhan khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return Json(new { success = false, message = "Noi dung tin nhan khong duoc de trong." });
        }

        var message = await _context.EmployeeMessages
            .FirstOrDefaultAsync(m => m.Id == dto.Id && m.EmployeeUserId == user.Id && m.SenderRole == "Employee", cancellationToken);

        if (message == null)
        {
            return Json(new { success = false, message = "Khong tim thay tin nhan de chinh sua." });
        }

        if (message.IsRevoked)
        {
            return Json(new { success = false, message = "Tin nhan da bi thu hoi." });
        }

        message.Content = dto.Content.Trim();
        message.EditedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyConversationChangedAsync(user, message.Channel);
        return Json(new { success = true, message = "Da cap nhat tin nhan." });
    }

    [HttpPost]
    public async Task<IActionResult> RevokeMessage([FromBody] MessageActionRequest dto, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        if (dto.Id <= 0)
        {
            return Json(new { success = false, message = "Tin nhan khong hop le." });
        }

        var message = await _context.EmployeeMessages
            .FirstOrDefaultAsync(m => m.Id == dto.Id && m.EmployeeUserId == user.Id && m.SenderRole == "Employee", cancellationToken);

        if (message == null)
        {
            return Json(new { success = false, message = "Khong tim thay tin nhan de thu hoi." });
        }

        if (message.IsRevoked)
        {
            return Json(new { success = false, message = "Tin nhan nay da duoc thu hoi truoc do." });
        }

        message.IsRevoked = true;
        message.RevokedAtUtc = DateTime.UtcNow;
        message.Content = "Tin nhan da duoc thu hoi.";

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyConversationChangedAsync(user, message.Channel);
        return Json(new { success = true, message = "Da thu hoi tin nhan." });
    }
    [HttpPost]
    public async Task<IActionResult> UpdatePayrollPin([FromBody] UpdatePinRequest req, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });

        if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length < 4)
            return Json(new { success = false, message = "Ma PIN phai co it nhat 4 ky tu." });

        user.PayrollPin = req.Pin;
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Cap nhat ma PIN thanh cong." });
    }

    [HttpPost]
    public async Task<IActionResult> VerifyPayrollPin([FromBody] VerifyPinRequest req, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });

        if (string.IsNullOrEmpty(user.PayrollPin))
            return Json(new { success = false, message = "Chua dang ky ma PIN." });

        if (user.PayrollPin != req.Pin)
            return Json(new { success = false, message = "Ma PIN khong dung." });

        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetChatContacts(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var contacts = await _context.Users
            .Where(u => u.Id != user.Id)
            .Select(u => new
            {
                username = u.Username,
                fullName = u.FullName ?? u.Username,
                role = u.Role ?? "Employee",
                avatarUrl = string.IsNullOrWhiteSpace(u.AvatarPath) ? null : $"{u.AvatarPath}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = contacts });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyMessages(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var messages = await _context.EmployeeMessages
            .Where(m => m.EmployeeUserId == user.Id 
                     || m.Channel == user.Id.ToString() 
                     || m.Channel == user.Username)
            .OrderBy(m => m.SentAt)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = messages });
    }

    [HttpGet]
    public async Task<IActionResult> GetKnowledgeBase(CancellationToken cancellationToken)
    {
        var items = await _context.KnowledgeBaseItems
            .Where(i => i.IsPublished)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = items });
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var messages = await _context.EmployeeMessages
            .Where(m => m.EmployeeUserId == user.Id && m.SenderRole != "Employee")
            .OrderByDescending(m => m.SentAt)
            .Take(10)
            .Select(m => new
            {
                source = "message",
                id = m.Id,
                title = string.IsNullOrWhiteSpace(m.Title) ? $"Tin nhan tu {m.SenderName}" : m.Title,
                body = m.Content,
                createdAt = m.SentAt,
                isRead = m.IsRead,
                tab = "messages"
            })
            .ToListAsync(cancellationToken);

        var requestUpdates = await _context.ApprovalRequests
            .Where(r => r.EmployeeUserId == user.Id && r.Status != "Chờ duyệt" && r.Status != "Pending")
            .OrderByDescending(r => r.SubmittedAt)
            .Take(10)
            .Select(r => new
            {
                source = "request",
                id = r.Id,
                title = $"Don tu: {r.RequestType}",
                body = $"Trang thai hien tai: {r.Status}",
                createdAt = r.SubmittedAt,
                isRead = true,
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
            unreadCount = messages.Count(m => !m.isRead)
        });
    }

    [HttpPost]
    public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var message = await _context.EmployeeMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeUserId == user.Id, cancellationToken);

        if (message == null)
        {
            return Json(new { success = false, message = "Khong tim thay thong bao." });
        }

        message.IsRead = true;
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        }

        var unreadMessages = await _context.EmployeeMessages
            .Where(m => m.EmployeeUserId == user.Id && m.SenderRole != "Employee" && !m.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadMessages.Count > 0)
        {
            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return Json(new { success = true });
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst("UserId")?.Value;
        if (Guid.TryParse(userIdValue, out var userId))
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        var username = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(username)
            ? null
            : await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    private async Task NotifyConversationChangedAsync(User employee, string managerUsername)
    {
        var groups = new[]
        {
            InternalChatHub.BuildUsernameGroup(employee.Username),
            InternalChatHub.BuildUserIdGroup(employee.Id.ToString()),
            InternalChatHub.BuildUsernameGroup(managerUsername)
        };

        await _chatHub.Clients.Groups(groups).SendAsync("MessagesChanged", new
        {
            employeeUserId = employee.Id,
            employeeUsername = employee.Username,
            channel = managerUsername
        });
    }

    private async Task<string> SaveAttendanceImageAsync(Guid userId, string? imageDataUrl, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return string.Empty;
        }

        var commaIndex = imageDataUrl.IndexOf(',');
        var base64Payload = commaIndex >= 0 ? imageDataUrl[(commaIndex + 1)..] : imageDataUrl;
        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(base64Payload);
        }
        catch
        {
            return string.Empty;
        }

        var directory = Path.Combine(_environment.WebRootPath, "uploads", "attendance", userId.ToString("N"));
        Directory.CreateDirectory(directory);
        var fileName = $"{prefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.jpg";
        var fullPath = Path.Combine(directory, fileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
        return $"/uploads/attendance/{userId:N}/{fileName}";
    }

    private static object MapSession(WorkSession session)
    {
        return new
        {
            id = session.Id.ToString(CultureInfo.InvariantCulture),
            employeeCode = session.EmployeeCode,
            employeeName = session.EmployeeName,
            date = session.Date,
            checkInAt = session.CheckInTime,
            checkOutAt = session.CheckOutTime,
            checkInImage = session.CheckInImagePath,
            checkOutImage = session.CheckOutImagePath,
            status = session.Status
        };
    }

    public sealed class UpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Department { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public sealed class UpdateSettingsRequest
    {
        public bool Notifications { get; set; }
        public bool Compact { get; set; }
        public bool ReducedMotion { get; set; }
        public string Language { get; set; } = "vi-VN";
        public string Theme { get; set; } = "light";
    }

    public sealed class AttendanceCaptureRequest
    {
        public string? ImageDataUrl { get; set; }
    }

    public sealed class SendMessageRequest
    {
        public string Channel { get; set; } = "manager";
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public sealed class EditMessageRequest
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public sealed class MessageActionRequest
    {
        public int Id { get; set; }
    }

    public sealed class SendApprovalRequestRequest
    {
        public int TemplateId { get; set; }
        public string RequestedDate { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class UpdatePinRequest
    {
        public string Pin { get; set; } = string.Empty;
    }

    public sealed class VerifyPinRequest
    {
        public string Pin { get; set; } = string.Empty;
    }


}
