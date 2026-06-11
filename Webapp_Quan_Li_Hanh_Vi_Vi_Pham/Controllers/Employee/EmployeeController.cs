using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers.Employee;

[Authorize(Roles = "Employee")]
public class EmployeeController : Controller
{
    private readonly ViolationDbContext _context;

    public EmployeeController(ViolationDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetMyViolations(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Json(new { success = false, message = "Người dùng không hợp lệ." });
        }

        // Lấy danh sách vi phạm của chính nhân viên này dựa trên EmployeeCode trùng với username
        var violations = await _context.ViolationRecords
            .Where(v => v.EmployeeCode == username)
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(10) // Lấy tối đa 10 vi phạm gần đây nhất
            .ToListAsync(cancellationToken);

        return Json(new { success = true, data = violations });
    }

    public IActionResult Attendance()
    {
        return RedirectToAction(nameof(Index), new { tab = "attendance" });
    }

    public IActionResult Messages()
    {
        return RedirectToAction(nameof(Index), new { tab = "messages" });
    }

    public IActionResult Requests()
    {
        return RedirectToAction(nameof(Index), new { tab = "requests" });
    }

    public IActionResult Profile()
    {
        return RedirectToAction(nameof(Index), new { tab = "profile" });
    }

    public IActionResult Settings()
    {
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }

    public IActionResult Payroll()
    {
        return RedirectToAction(nameof(Index), new { tab = "payroll" });
    }

    public IActionResult KnowledgeBase()
    {
        return RedirectToAction(nameof(Index), new { tab = "knowledge" });
    }

    public IActionResult Violations()
    {
        return RedirectToAction(nameof(Index), new { tab = "violations" });
    }
}
