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

    [HttpGet]
    public async Task<IActionResult> GetMyTasks(CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Không xác định được danh tính." });
        }

        var tasks = await _context.EmployeeTasks
            .Where(t => t.EmployeeId == userId)
            .OrderByDescending(t => t.DueDate)
            .ToListAsync(cancellationToken);
            
        return Json(new { success = true, data = tasks });
    }

    [HttpPost]
    public async Task<IActionResult> MarkTaskDone(Guid id, CancellationToken cancellationToken)
    {
        var task = await _context.EmployeeTasks.FindAsync(new object[] { id }, cancellationToken);
        if (task != null)
        {
            task.Status = "Done";
            await _context.SaveChangesAsync(cancellationToken);
            return Json(new { success = true });
        }
        return Json(new { success = false });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyPayrolls(int year, CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirst("UserId")?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Json(new { success = false, message = "Không xác định được danh tính." });
        }

        var payrolls = await _context.PayrollRecords
            .Where(p => p.EmployeeId == userId && p.Year == year)
            .OrderByDescending(p => p.Month)
            .ToListAsync(cancellationToken);
            
        return Json(new { success = true, data = payrolls });
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
