using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public class ViolationService : IViolationService
{
    private readonly IYoloInferenceService _yoloInferenceService;
    private readonly ViolationDbContext _context;

    public ViolationService(IYoloInferenceService yoloInferenceService, ViolationDbContext context)
    {
        _yoloInferenceService = yoloInferenceService;
        _context = context;
    }

    public async Task<ViolationDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var latestDetections = await _yoloInferenceService.GetLatestDetectionsAsync(cancellationToken);
        
        var recentViolations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var totalToday = await _context.ViolationRecords
            .CountAsync(v => v.DetectedAtUtc >= today, cancellationToken);

        var pendingCount = await _context.ViolationRecords
            .CountAsync(v => v.Status == "Pending", cancellationToken);

        var highSeverityCount = await _context.ViolationRecords
            .CountAsync(v => v.Severity == "High", cancellationToken);

        return new ViolationDashboardViewModel
        {
            TotalViolationsToday = totalToday,
            PendingReviews = pendingCount,
            HighSeverityCases = highSeverityCount,
            RecentViolations = recentViolations,
            LatestDetections = latestDetections
        };
    }

    public async Task<List<ViolationRecord>> GetAllViolationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddViolationAsync(ViolationRecord record, CancellationToken cancellationToken = default)
    {
        record.Id = Guid.NewGuid();
        record.DetectedAtUtc = DateTime.UtcNow;
        _context.ViolationRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateViolationStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        var record = await _context.ViolationRecords.FindAsync(new object[] { id }, cancellationToken);
        if (record != null)
        {
            record.Status = status;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
