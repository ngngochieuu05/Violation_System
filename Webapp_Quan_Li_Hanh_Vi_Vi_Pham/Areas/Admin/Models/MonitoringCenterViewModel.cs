using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Models;

public class MonitoringCenterViewModel
{
    public int PollingIntervalSeconds { get; set; }
    public int SmokeDetectionThresholdCount { get; set; }
    public int EmptyChairThresholdMinutes { get; set; }
    public string CameraLocation { get; set; } = string.Empty;
    public bool TelegramEnabled { get; set; }
    public string ConfiguredChatIds { get; set; } = string.Empty;
    public string KnownChatIds { get; set; } = string.Empty;
    public IReadOnlyCollection<TelegramChatUpdate> RecentTelegramUpdates { get; set; } = [];
    public IReadOnlyCollection<ViolationRecord> RecentViolations { get; set; } = [];
    public ViolationAlertResult? LastAlertResult { get; set; }
    public TelegramSendResult? LastTelegramSendResult { get; set; }
}
