using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public class ViolationService : IViolationService
{
    private readonly IYoloInferenceService _yoloInferenceService;

    public ViolationService(IYoloInferenceService yoloInferenceService)
    {
        _yoloInferenceService = yoloInferenceService;
    }

    public async Task<ViolationDashboardViewModel> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var latestDetections = await _yoloInferenceService.GetLatestDetectionsAsync(cancellationToken);

        var recentViolations = new List<ViolationRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV001",
                EmployeeName = "Nguyen Van A",
                ViolationType = "Khong doi mu bao ho",
                Severity = "High",
                DetectedAtUtc = DateTime.UtcNow.AddMinutes(-25),
                CameraLocation = "Xuong A - Cong vao",
                EvidenceUrl = "/evidence/sample-1.jpg",
                Status = "Pending"
            },
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV014",
                EmployeeName = "Tran Thi B",
                ViolationType = "Vao khu vuc han che",
                Severity = "Medium",
                DetectedAtUtc = DateTime.UtcNow.AddHours(-2),
                CameraLocation = "Kho B",
                EvidenceUrl = "/evidence/sample-2.jpg",
                Status = "Reviewed"
            }
        };

        return new ViolationDashboardViewModel
        {
            TotalViolationsToday = recentViolations.Count,
            PendingReviews = recentViolations.Count(x => x.Status == "Pending"),
            HighSeverityCases = recentViolations.Count(x => x.Severity == "High"),
            RecentViolations = recentViolations,
            LatestDetections = latestDetections
        };
    }
}
