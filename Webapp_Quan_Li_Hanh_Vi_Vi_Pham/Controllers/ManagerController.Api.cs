using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

public partial class ManagerController
{
    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return null;
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        return Json(new {
            success = true,
            data = new {
                user.Id,
                user.Username,
                user.FullName,
                user.Email,
                user.Phone,
                user.Department,
                user.EmployeeCode,
                user.Role,
                hasPayrollPin = !string.IsNullOrEmpty(user.PayrollPin),
                avatarUrl = string.IsNullOrWhiteSpace(user.AvatarPath) ? null : $"{user.AvatarPath}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            }
        });
    }

    public sealed class ManagerUpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Department { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile([FromBody] ManagerUpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
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
        if (user == null) return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".jfif" };
        byte[] imageBytes;
        string extension;

        if (avatar != null && avatar.Length > 0)
        {
            extension = Path.GetExtension(avatar.FileName);
            if (!allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)) return Json(new { success = false, message = "Dinh dang anh khong duoc ho tro." });
            await using var memory = new MemoryStream();
            await avatar.CopyToAsync(memory, cancellationToken);
            imageBytes = memory.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(avatarBase64))
        {
            extension = Path.GetExtension(fileName ?? ".jpg");
            if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)) extension = ".jpg";
            var commaIndex = avatarBase64.IndexOf(',');
            var payload = commaIndex >= 0 ? avatarBase64[(commaIndex + 1)..] : avatarBase64;
            try { imageBytes = Convert.FromBase64String(payload); }
            catch { return Json(new { success = false, message = "Anh dai dien khong hop le." }); }
        }
        else return Json(new { success = false, message = "Khong co du lieu anh." });

        if (imageBytes.Length > 5 * 1024 * 1024) return Json(new { success = false, message = "Kich thuoc anh vuot qua 5MB." });

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
        Directory.CreateDirectory(uploadsFolder);
        var newFileName = $"avatar_{user.Id}{extension}";
        var filePath = Path.Combine(uploadsFolder, newFileName);

        if (!string.IsNullOrWhiteSpace(user.AvatarPath))
        {
            var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.AvatarPath.TrimStart('/'));
            if (System.IO.File.Exists(oldPath) && !oldPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.File.Delete(oldPath); } catch { }
            }
        }

        await System.IO.File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);
        user.AvatarPath = $"/uploads/avatars/{newFileName}";
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Da cap nhat anh dai dien." });
    }

    public sealed class ManagerUpdatePinRequest { public string Pin { get; set; } = string.Empty; }
    public sealed class ManagerVerifyPinRequest { public string Pin { get; set; } = string.Empty; }

    [HttpPost]
    public async Task<IActionResult> UpdatePayrollPin([FromBody] ManagerUpdatePinRequest req, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length < 4) return Json(new { success = false, message = "Ma PIN phai co it nhat 4 ky tu." });
        user.PayrollPin = req.Pin;
        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Cap nhat ma PIN thanh cong." });
    }

    [HttpPost]
    public async Task<IActionResult> VerifyPayrollPin([FromBody] ManagerVerifyPinRequest req, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null) return Json(new { success = false, message = "Khong xac dinh duoc tai khoan." });
        if (string.IsNullOrEmpty(user.PayrollPin)) return Json(new { success = false, message = "Chua dang ky ma PIN." });
        if (user.PayrollPin != req.Pin) return Json(new { success = false, message = "Ma PIN khong dung." });
        return Json(new { success = true });
    }
    public sealed class ManagerSendMessageRequest { 
        public Guid EmployeeId { get; set; } 
        public string Title { get; set; } = string.Empty; 
        public string Content { get; set; } = string.Empty; 
    }

    public sealed class ManagerEditMessageRequest
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ManagerSendMessageRequest req, CancellationToken cancellationToken)
    {
        var manager = await GetCurrentUserAsync(cancellationToken);
        if (manager == null) return Json(new { success = false, message = "Không xác định được tài khoản quản lý." });
        
        var employee = await _context.Users.FindAsync(new object[] { req.EmployeeId }, cancellationToken);
        if (employee == null) return Json(new { success = false, message = "Không tìm thấy nhân viên." });

        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Content))
            return Json(new { success = false, message = "Vui lòng nhập đầy đủ tiêu đề và nội dung." });

        var msg = new EmployeeMessage
        {
            EmployeeUserId = employee.Id,
            EmployeeUsername = employee.Username,
            EmployeeName = employee.FullName,
            Channel = manager.Username, // Use manager's username so Employee can group it correctly
            SenderRole = "Manager",
            SenderName = manager.FullName,
            Title = req.Title,
            Content = req.Content,
            SentAt = DateTime.UtcNow,
            IsRead = false
        };

        _context.EmployeeMessages.Add(msg);
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true, message = "Đã gửi thông báo thành công." });
    }

    [HttpPost]
    public async Task<IActionResult> EditMessage([FromBody] ManagerEditMessageRequest req, CancellationToken cancellationToken)
    {
        var manager = await GetCurrentUserAsync(cancellationToken);
        if (manager == null) return Json(new { success = false, message = "KhÃ´ng xÃ¡c Ä‘á»‹nh Ä‘Æ°á»£c tÃ i khoáº£n quáº£n lÃ½." });

        if (req.Id <= 0 || string.IsNullOrWhiteSpace(req.Content))
            return Json(new { success = false, message = "Dá»¯ liá»‡u chá»‰nh sá»­a khÃ´ng há»£p lá»‡." });

        var msg = await _context.EmployeeMessages.FindAsync(new object[] { req.Id }, cancellationToken);
        if (msg == null) return Json(new { success = false, message = "KhÃ´ng tÃ¬m tháº¥y tin nháº¯n." });

        if (msg.SenderRole != "Manager" || msg.SenderName != manager.FullName)
            return Json(new { success = false, message = "Báº¡n khÃ´ng cÃ³ quyá»n chá»‰nh sá»­a tin nháº¯n nÃ y." });

        if (msg.IsRevoked)
            return Json(new { success = false, message = "Tin nháº¯n Ä‘Ã£ bá»‹ thu há»“i, khÃ´ng thá»ƒ chá»‰nh sá»­a." });

        msg.Content = req.Content.Trim();
        msg.EditedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true, message = "ÄÃ£ cáº­p nháº­t tin nháº¯n." });
    }

    [HttpPost]
    public async Task<IActionResult> RevokeMessage(int id, CancellationToken cancellationToken)
    {
        var manager = await GetCurrentUserAsync(cancellationToken);
        if (manager == null) return Json(new { success = false, message = "Không xác định được tài khoản quản lý." });

        var msg = await _context.EmployeeMessages.FindAsync(new object[] { id }, cancellationToken);
        if (msg == null) return Json(new { success = false, message = "Không tìm thấy tin nhắn." });

        if (msg.SenderRole != "Manager" || msg.SenderName != manager.FullName)
            return Json(new { success = false, message = "Bạn không có quyền thu hồi tin nhắn này." });

        if (msg.IsRevoked) return Json(new { success = false, message = "Tin nhắn đã bị thu hồi trước đó." });

        msg.IsRevoked = true;
        msg.RevokedAtUtc = DateTime.UtcNow;
        msg.Content = "[Tin nhắn đã thu hồi]";
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new { success = true });
    }
}
