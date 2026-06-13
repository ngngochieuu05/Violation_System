using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[Route("api/public")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly ViolationDbContext _context;

    public ApiController(ViolationDbContext context)
    {
        _context = context;
    }

    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            App = "Hệ thống Quản lý Hành vi",
            Version = "1.0",
            Status = "Online"
        });
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        var employees = await _context.Users
            .Where(u => u.Role == "Employee" && u.FaceImagePath != null && u.FaceImagePath != "")
            .Select(u => new { u.Id, u.Username, u.FullName, u.Role })
            .ToListAsync(cancellationToken);
            
        return Ok(new { success = true, data = employees });
    }

    [HttpGet("violations")]
    public async Task<IActionResult> GetViolations(CancellationToken cancellationToken)
    {
        var violations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(20)
            .Select(v => new { v.Id, v.EmployeeCode, v.ViolationType, v.Severity, v.DetectedAtUtc })
            .ToListAsync(cancellationToken);
            
        return Ok(new { success = true, data = violations });
    }

    // Chặn lệnh POST chỉ được phép GET thông tin cơ bản
    [HttpPost]
    [HttpPost("{*path}")]
    public IActionResult BlockPost()
    {
        return StatusCode(403, new { 
            success = false, 
            message = "Lệnh POST đã bị chặn theo yêu cầu. Bạn chỉ được phép dùng phương thức GET để lấy các thông tin cơ bản." 
        });
    }
}
