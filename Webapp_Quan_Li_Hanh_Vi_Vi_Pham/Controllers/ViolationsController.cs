using Microsoft.AspNetCore.Mvc;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

public class ViolationsController : Controller
{
    private readonly IViolationService _violationService;

    public ViolationsController(IViolationService violationService)
    {
        _violationService = violationService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _violationService.GetDashboardAsync(cancellationToken);
        return View(dashboard);
    }
}
