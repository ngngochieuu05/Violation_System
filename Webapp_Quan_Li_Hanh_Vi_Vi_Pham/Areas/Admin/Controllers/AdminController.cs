using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IModelSettingService _modelSettingService;
    private readonly ViolationDbContext _context;

    public AdminController(IModelSettingService modelSettingService, ViolationDbContext context)
    {
        _modelSettingService = modelSettingService;
        _context = context;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeSetting = await _modelSettingService.GetActiveSettingAsync(cancellationToken);
        
        var managers = await _context.Users
            .Where(u => u.Role == "Manager")
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var aiModels = await _context.AiModels
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        ViewBag.Managers = managers;
        ViewBag.AiModels = aiModels;
        return View(activeSetting);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateModelSettings(
        string yoloModelPath, 
        decimal yoloConfThreshold, 
        decimal yoloIouThreshold, 
        decimal deepfaceConfThreshold,
        CancellationToken cancellationToken)
    {
        var setting = new ModelSetting
        {
            YoloModelPath = yoloModelPath,
            YoloConfThreshold = yoloConfThreshold,
            YoloIouThreshold = yoloIouThreshold,
            DeepfaceConfThreshold = deepfaceConfThreshold
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
            TempData["SuccessMessage"] = $"Đã cập nhật khóa kích hoạt cho {manager.FullName}!";
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
            TempData["SuccessMessage"] = $"Đã reset trạng thái kích hoạt cho {manager.FullName}!";
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> AddAiModel(string name, string type, string modelPath, string confThreshold, string iouThreshold, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(modelPath))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập đầy đủ thông tin mô hình.";
            return RedirectToAction("Index");
        }

        decimal.TryParse(confThreshold?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedConf);
        decimal.TryParse(iouThreshold?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedIou);

        var newModel = new AiModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            ModelPath = modelPath,
            ConfThreshold = parsedConf,
            IouThreshold = parsedIou,
            IsActive = false, // starts as inactive, user can toggle active
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.AiModels.Add(newModel);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = $"Đã thêm mô hình {name} thành công!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> EditAiModel(
        Guid id, 
        string name, 
        string modelPath, 
        string confThreshold, 
        string iouThreshold, 
        CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction("Index");
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(modelPath))
        {
            TempData["ErrorMessage"] = "Vui lòng điền đầy đủ tên và đường dẫn mô hình.";
            return RedirectToAction("Index");
        }

        decimal.TryParse(confThreshold?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedConf);
        decimal.TryParse(iouThreshold?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedIou);

        model.Name = name;
        model.ModelPath = modelPath;
        model.ConfThreshold = parsedConf;
        model.IouThreshold = parsedIou;

        await _context.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = $"Đã cập nhật thông số mô hình {name} thành công!";
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> ToggleAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction("Index");
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
            TempData["SuccessMessage"] = $"Đã ngắt kích hoạt mô hình {model.Name}!";
        }

        await _context.SaveChangesAsync(cancellationToken);
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mô hình AI.";
            return RedirectToAction("Index");
        }

        if (model.IsActive)
        {
            TempData["ErrorMessage"] = "Không thể xóa mô hình đang ở trạng thái kích hoạt.";
            return RedirectToAction("Index");
        }

        _context.AiModels.Remove(model);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = $"Đã xóa mô hình {model.Name} thành công!";
        return RedirectToAction("Index");
    }
}
