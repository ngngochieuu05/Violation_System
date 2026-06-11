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
[Route("Admin")]
public class AdminController : Controller
{
    private readonly IModelSettingService _modelSettingService;
    private readonly ViolationDbContext _context;

    public AdminController(IModelSettingService modelSettingService, ViolationDbContext context)
    {
        _modelSettingService = modelSettingService;
        _context = context;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var activeSetting = await _modelSettingService.GetActiveSettingAsync(cancellationToken);
        var managers = await _context.Users.Where(u => u.Role == "Manager").OrderByDescending(u => u.CreatedAtUtc).ToListAsync(cancellationToken);
        var aiModels = await _context.AiModels.OrderByDescending(m => m.CreatedAtUtc).ToListAsync(cancellationToken);

        ViewBag.Managers = managers;
        ViewBag.AiModels = aiModels;
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

    // --- CÁC TRANG MỚI ---
    [HttpGet("Personnel")]
    public async Task<IActionResult> Personnel(CancellationToken cancellationToken)
    {
        var managers = await _context.Users
            .Where(u => u.Role == "Manager")
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return View("Personnel", managers);
    }

    [HttpGet("AuditLogs")]
    public IActionResult AuditLogs()
    {
        return View("AuditLogs");
    }

    [HttpGet("Settings")]
    public IActionResult Settings()
    {
        return View("Settings");
    }

    // --- CÁC HÀM XỬ LÝ (GIỮ NGUYÊN) ---
    private string GetRedirectUrl()
    {
        string referer = Request.Headers["Referer"].ToString();
        return string.IsNullOrEmpty(referer) ? "/Admin" : referer;
    }

    [HttpPost("[action]")]
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
        TempData["SuccessMessage"] = "Cập nhật cấu hình AI thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> UpdateManagerKey(Guid managerId, string newKey, CancellationToken cancellationToken)
    {
        var manager = await _context.Users.FindAsync(new object[] { managerId }, cancellationToken);
        if (manager != null && manager.Role == "Manager")
        {
            manager.ManagerKey = newKey;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = $"Đã cập nhật khóa cho {manager.FullName}!";
        }
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> ResetManagerActivation(Guid managerId, CancellationToken cancellationToken)
    {
        var manager = await _context.Users.FindAsync(new object[] { managerId }, cancellationToken);
        if (manager != null && manager.Role == "Manager")
        {
            manager.IsKeyActivated = false;
            await _context.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = $"Đã reset thiết bị cho {manager.FullName}!";
        }
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
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
        TempData["SuccessMessage"] = $"Đã thêm mô hình {name} thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> EditAiModel(Guid id, string name, string modelPath, decimal confThreshold, decimal iouThreshold, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null) return Redirect(GetRedirectUrl());

        model.Name = name;
        model.ModelPath = modelPath;
        model.ConfThreshold = confThreshold;
        model.IouThreshold = iouThreshold;
        await _context.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = $"Đã cập nhật mô hình {name} thành công!";
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
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
        return Redirect(GetRedirectUrl());
    }

    [HttpPost("[action]")]
    public async Task<IActionResult> DeleteAiModel(Guid id, CancellationToken cancellationToken)
    {
        var model = await _context.AiModels.FindAsync(new object[] { id }, cancellationToken);
        if (model == null || model.IsActive) return Redirect(GetRedirectUrl());

        _context.AiModels.Remove(model);
        await _context.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = $"Đã xóa mô hình {model.Name}!";
        return Redirect(GetRedirectUrl());
    }
}