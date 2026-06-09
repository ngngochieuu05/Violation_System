using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers.Employee;

[Authorize(Roles = "Employee")]
public class EmployeeController : Controller
{
    public IActionResult Index()
    {
        return View();
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
}
