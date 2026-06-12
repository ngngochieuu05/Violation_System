using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationMonitoringOrchestrator : IViolationMonitoringOrchestrator
{
    private static readonly HashSet<string> SmokeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "smoke",
        "smoking"
    };

    private static readonly HashSet<string> EmptyChairLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "empty-chair",
        "non-human",
        "empty_seat"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ViolationMonitoringOptions _options;
    private readonly ILogger<ViolationMonitoringOrchestrator> _logger;
    private readonly Dictionary<string, TrackedDetection> _smokeTracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedDetection> _emptyChairTracks = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    public ViolationMonitoringOrchestrator(
        IServiceScopeFactory scopeFactory,
        IOptions<ViolationMonitoringOptions> options,
        ILogger<ViolationMonitoringOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<ViolationAlertResult>> ProcessDetectionsAsync(
        IReadOnlyCollection<DetectionResult> detections,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        List<PendingAlert> alertsToPublish;

        lock (_syncLock)
        {
            var smokeDetections = detections
                .Where(d => string.Equals(d.ModelType, "YoloSmoking", StringComparison.OrdinalIgnoreCase))
                .Where(d => SmokeLabels.Contains(d.Label))
                .ToList();

            var emptyChairDetections = detections
                .Where(d => string.Equals(d.ModelType, "YoloLeaving", StringComparison.OrdinalIgnoreCase))
                .Where(d => EmptyChairLabels.Contains(d.Label))
                .ToList();

            alertsToPublish = [];
            TrackDetections(_smokeTracks, smokeDetections, nowUtc, isSmokeTrack: true, alertsToPublish);
            TrackDetections(_emptyChairTracks, emptyChairDetections, nowUtc, isSmokeTrack: false, alertsToPublish);
            PruneStaleTracks(_smokeTracks, nowUtc);
            PruneStaleTracks(_emptyChairTracks, nowUtc);
        }

        return await PublishAlertsAsync(alertsToPublish, cancellationToken);
    }

    public Task<ViolationAlertResult> TriggerSmokeTestAsync(CancellationToken cancellationToken = default)
    {
        return PublishSingleManualAlertAsync(
            new PendingAlert(
                $"SMK-TEST-{DateTime.UtcNow:HHmmss}",
                "Hút thuốc tại khu vực làm việc",
                "High",
                DateTime.UtcNow,
                "/evidence/test-smoke.jpg",
                $"[TESTCASE HÚT THUỐC] Track test mô phỏng vượt ngưỡng {_options.SmokeDetectionThresholdCount} lần tại {_options.CameraLocation}."),
            cancellationToken);
    }

    public Task<ViolationAlertResult> TriggerLeavingPositionTestAsync(CancellationToken cancellationToken = default)
    {
        return PublishSingleManualAlertAsync(
            new PendingAlert(
                $"LEAVE-TEST-{DateTime.UtcNow:HHmmss}",
                "Rời vị trí làm việc",
                "Medium",
                DateTime.UtcNow,
                "/evidence/test-leaving.jpg",
                $"[TESTCASE RỜI VỊ TRÍ] Ghế trống/non-human được mô phỏng duy trì quá {_options.EmptyChairThresholdMinutes} phút tại {_options.CameraLocation}."),
            cancellationToken);
    }

    private async Task<ViolationAlertResult> PublishSingleManualAlertAsync(PendingAlert alert, CancellationToken cancellationToken)
    {
        var results = await PublishAlertsAsync([alert], cancellationToken);
        return results.Single();
    }

    private async Task<IReadOnlyCollection<ViolationAlertResult>> PublishAlertsAsync(
        IReadOnlyCollection<PendingAlert> alertsToPublish,
        CancellationToken cancellationToken)
    {
        if (alertsToPublish.Count == 0)
        {
            return [];
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();
        var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramAlertService>();
        var results = new List<ViolationAlertResult>(alertsToPublish.Count);

        foreach (var alert in alertsToPublish)
        {
            var violation = new ViolationRecord
            {
                Id = Guid.NewGuid(),
                TrackingId = alert.TrackId,
                EmployeeCode = alert.TrackId,
                EmployeeName = "Hệ thống giám sát",
                ViolationType = alert.ViolationType,
                Severity = alert.Severity,
                DetectedAtUtc = alert.DetectedAtUtc,
                CameraLocation = _options.CameraLocation,
                EvidenceUrl = alert.EvidenceUrl,
                Status = "Pending"
            };

            dbContext.ViolationRecords.Add(violation);
            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = alert.DetectedAtUtc,
                Username = "System",
                Action = alert.ViolationType,
                Details = alert.Message,
                IpAddress = "127.0.0.1",
                Status = "Cảnh báo"
            });

            results.Add(new ViolationAlertResult
            {
                ViolationId = violation.Id,
                TrackId = alert.TrackId,
                ViolationType = alert.ViolationType,
                Severity = alert.Severity,
                DetectedAtUtc = alert.DetectedAtUtc,
                Message = alert.Message
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var result in results)
        {
            try
            {
                var violation = await dbContext.ViolationRecords.FirstAsync(v => v.Id == result.ViolationId, cancellationToken);
                await telegramService.SendViolationAlertAsync(violation, result.Message, cancellationToken);
                result.TelegramAttempted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Telegram alert for {TrackId}", result.TrackId);
            }
        }

        return results;
    }

    private void TrackDetections(
        Dictionary<string, TrackedDetection> tracks,
        List<DetectionResult> detections,
        DateTime nowUtc,
        bool isSmokeTrack,
        List<PendingAlert> alertsToPublish)
    {
        var matchedTrackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detection in detections)
        {
            var box = BoundingBoxInfo.Parse(detection.BoundingBox);
            var bestTrack = tracks.Values
                .Where(track => !matchedTrackIds.Contains(track.TrackId))
                .Select(track => new
                {
                    Track = track,
                    Score = BoundingBoxInfo.CalculateIoU(track.BoundingBox, box)
                })
                .Where(item => item.Score >= _options.TrackMatchIouThreshold)
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();

            TrackedDetection track;
            if (bestTrack is null)
            {
                track = new TrackedDetection
                {
                    TrackId = $"{(isSmokeTrack ? "SMK" : "LEAVE")}-{Guid.NewGuid():N}"[..16],
                    BoundingBox = box,
                    FirstSeenUtc = nowUtc,
                    LastSeenUtc = nowUtc,
                    SeenCount = 1,
                    Label = detection.Label
                };
                tracks[track.TrackId] = track;
            }
            else
            {
                track = bestTrack.Track;
                track.BoundingBox = box;
                track.LastSeenUtc = nowUtc;
                track.SeenCount++;
                track.Label = detection.Label;
            }

            detection.TrackId = track.TrackId;
            matchedTrackIds.Add(track.TrackId);

            if (track.AlertRaised)
            {
                continue;
            }

            if (isSmokeTrack && track.SeenCount >= _options.SmokeDetectionThresholdCount)
            {
                track.AlertRaised = true;
                alertsToPublish.Add(BuildSmokeAlert(track, nowUtc));
            }

            if (!isSmokeTrack && nowUtc - track.FirstSeenUtc >= TimeSpan.FromMinutes(_options.EmptyChairThresholdMinutes))
            {
                track.AlertRaised = true;
                alertsToPublish.Add(BuildLeavingAlert(track, nowUtc));
            }
        }
    }

    private void PruneStaleTracks(Dictionary<string, TrackedDetection> tracks, DateTime nowUtc)
    {
        var maxSilence = TimeSpan.FromMinutes(1);
        var staleTrackIds = tracks.Values
            .Where(track => nowUtc - track.LastSeenUtc > maxSilence)
            .Select(track => track.TrackId)
            .ToList();

        foreach (var trackId in staleTrackIds)
        {
            tracks.Remove(trackId);
        }
    }

    private PendingAlert BuildSmokeAlert(TrackedDetection track, DateTime nowUtc)
    {
        return new PendingAlert(
            track.TrackId,
            "Hút thuốc tại khu vực làm việc",
            "High",
            nowUtc,
            "/evidence/monitoring-smoke.jpg",
            $"[CẢNH BÁO HÚT THUỐC] Track {track.TrackId} bị phát hiện khói thuốc {track.SeenCount} lần tại {_options.CameraLocation} lúc {nowUtc:yyyy-MM-dd HH:mm:ss}.");
    }

    private PendingAlert BuildLeavingAlert(TrackedDetection track, DateTime nowUtc)
    {
        var minutes = Math.Floor((nowUtc - track.FirstSeenUtc).TotalMinutes);
        return new PendingAlert(
            track.TrackId,
            "Rời vị trí làm việc",
            "Medium",
            nowUtc,
            "/evidence/monitoring-leave.jpg",
            $"[CẢNH BÁO RỜI VỊ TRÍ] Track {track.TrackId} chỉ phát hiện ghế trống/non-human trong {minutes} phút tại {_options.CameraLocation} lúc {nowUtc:yyyy-MM-dd HH:mm:ss}.");
    }

    private sealed class TrackedDetection
    {
        public required string TrackId { get; init; }
        public required BoundingBoxInfo BoundingBox { get; set; }
        public required DateTime FirstSeenUtc { get; init; }
        public required DateTime LastSeenUtc { get; set; }
        public int SeenCount { get; set; }
        public bool AlertRaised { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    private sealed record PendingAlert(
        string TrackId,
        string ViolationType,
        string Severity,
        DateTime DetectedAtUtc,
        string EvidenceUrl,
        string Message);

    private readonly record struct BoundingBoxInfo(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public static BoundingBoxInfo Parse(string raw)
        {
            var values = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(part => part.Length == 2)
                .ToDictionary(part => part[0], part => double.TryParse(part[1], out var value) ? value : 0d, StringComparer.OrdinalIgnoreCase);

            return new BoundingBoxInfo(
                values.GetValueOrDefault("x"),
                values.GetValueOrDefault("y"),
                values.GetValueOrDefault("w"),
                values.GetValueOrDefault("h"));
        }

        public static double CalculateIoU(BoundingBoxInfo left, BoundingBoxInfo right)
        {
            var overlapWidth = Math.Max(0d, Math.Min(left.Right, right.Right) - Math.Max(left.X, right.X));
            var overlapHeight = Math.Max(0d, Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y));
            var intersection = overlapWidth * overlapHeight;
            if (intersection <= 0d)
            {
                return 0d;
            }

            var union = (left.Width * left.Height) + (right.Width * right.Height) - intersection;
            return union <= 0d ? 0d : intersection / union;
        }
    }
}
