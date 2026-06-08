using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;

public class ViolationDashboardViewModel
{
    public int TotalViolationsToday { get; set; }
    public int PendingReviews { get; set; }
    public int HighSeverityCases { get; set; }
    public IReadOnlyCollection<ViolationRecord> RecentViolations { get; set; } = [];
    public IReadOnlyCollection<DetectionResult> LatestDetections { get; set; } = [];
}
