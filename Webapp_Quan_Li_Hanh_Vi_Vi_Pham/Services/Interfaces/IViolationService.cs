using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

public interface IViolationService
{
    Task<ViolationDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default);
}
