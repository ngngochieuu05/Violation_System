using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.ViewModels;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

public interface IViolationService
{
    Task<ViolationDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<List<ViolationRecord>> GetAllViolationsAsync(CancellationToken cancellationToken = default);
    Task<ViolationRecord?> GetViolationByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ViolationRecord?> GetViolationByTrackingIdAsync(string trackingId, CancellationToken cancellationToken = default);
    Task AddViolationAsync(ViolationRecord record, CancellationToken cancellationToken = default);
    Task UpdateViolationStatusAsync(Guid id, string status, CancellationToken cancellationToken = default);
    Task<bool> ReviewViolationAsync(Guid id, string status, string reviewer, string reviewChannel, string? reviewNote = null, CancellationToken cancellationToken = default);
    Task<bool> ReviewViolationByTrackingIdAsync(string trackingId, string status, string reviewer, string reviewChannel, string? reviewNote = null, CancellationToken cancellationToken = default);
}
