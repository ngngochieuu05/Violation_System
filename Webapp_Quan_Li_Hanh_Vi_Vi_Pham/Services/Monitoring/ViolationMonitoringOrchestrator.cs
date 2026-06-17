using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Utilities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationMonitoringOrchestrator : IViolationMonitoringOrchestrator
{
    private static readonly HashSet<string> SmokeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cigarette",
        "smoke",
        "smoking"
    };

    private static readonly HashSet<string> EmptyChairLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "un-occupied_desk",
        "empty-chair",
        "non-human",
        "empty_seat"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ViolationMonitoringOptions _options;
    private readonly ILogger<ViolationMonitoringOrchestrator> _logger;
    private readonly Dictionary<string, TrackedDetection> _smokeTracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedDetection> _emptyChairTracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedDetection> _previewSmokeTracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedDetection> _previewEmptyChairTracks = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    public ViolationMonitoringOrchestrator(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment environment,
        IOptions<ViolationMonitoringOptions> options,
        ILogger<ViolationMonitoringOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<ViolationAlertResult>> ProcessDetectionsAsync(
        IReadOnlyCollection<DetectionResult> detections,
        CancellationToken cancellationToken = default)
    {
        return ProcessDetectionsInternalAsync(detections, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    public Task<IReadOnlyCollection<ViolationAlertResult>> ProcessInferenceRunsAsync(
        IReadOnlyCollection<YoloInferenceRunResult> runs,
        CancellationToken cancellationToken = default)
    {
        var effectiveRuns = runs.Where(run => !run.IsMockResult).ToList();
        var detections = effectiveRuns.SelectMany(run => run.Detections).ToList();
        var evidenceByModelType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var run in effectiveRuns)
        {
            if (string.IsNullOrWhiteSpace(run.AnnotatedImageBase64))
            {
                continue;
            }

            var relativeUrl = SaveEvidenceImage(run.AnnotatedImageBase64, run.AnnotatedImageMimeType, run.ModelType, DateTime.UtcNow);
            if (!string.IsNullOrWhiteSpace(relativeUrl))
            {
                evidenceByModelType[run.ModelType] = relativeUrl;
            }
        }

        return ProcessDetectionsInternalAsync(detections, evidenceByModelType, cancellationToken);
    }

    public IReadOnlyCollection<YoloInferenceRunResult> AttachPreviewTracking(IReadOnlyCollection<YoloInferenceRunResult> runs)
    {
        var effectiveRuns = runs.ToList();
        var nowUtc = DateTime.UtcNow;

        lock (_syncLock)
        {
            ApplyTrackingToRuns(
                effectiveRuns,
                _previewSmokeTracks,
                nowUtc,
                isSmokeTrack: true,
                evidenceUrl: null,
                alertsToPublish: null);
            ApplyTrackingToRuns(
                effectiveRuns,
                _previewEmptyChairTracks,
                nowUtc,
                isSmokeTrack: false,
                evidenceUrl: null,
                alertsToPublish: null);
            PruneStaleTracks(_previewSmokeTracks, nowUtc);
            PruneStaleTracks(_previewEmptyChairTracks, nowUtc);
        }

        return effectiveRuns;
    }

    public Task<ViolationAlertResult> TriggerSmokeTestAsync(CancellationToken cancellationToken = default)
    {
        var cameraLocation = VietnameseText.NormalizeMojibake(_options.CameraLocation);
        return PublishSingleManualAlertAsync(
            new PendingAlert(
                $"SMK-TEST-{DateTime.UtcNow:HHmmss}",
                "Hút thuốc tại khu vực làm việc",
                "High",
                DateTime.UtcNow,
                "/evidence/test-smoke.jpg",
                $"[TESTCASE HÚT THUỐC] Track test mô phỏng vượt ngưỡng {_options.SmokeDetectionThresholdCount} lần tại {cameraLocation}."),
            cancellationToken);
    }

    public Task<ViolationAlertResult> TriggerLeavingPositionTestAsync(CancellationToken cancellationToken = default)
    {
        var threshold = _options.GetEmptyChairThreshold();
        var cameraLocation = VietnameseText.NormalizeMojibake(_options.CameraLocation);
        return PublishSingleManualAlertAsync(
            new PendingAlert(
                $"LEAVE-TEST-{DateTime.UtcNow:HHmmss}",
                "Rời vị trí làm việc",
                "Medium",
                DateTime.UtcNow,
                "/evidence/test-leaving.jpg",
                $"[TESTCASE RỜI VỊ TRÍ] Ghế trống/non-human được mô phỏng duy trì quá {threshold.TotalSeconds:0} giây tại {cameraLocation}."),
            cancellationToken);
    }

    public async Task<ViolationAlertResult> PublishInstantAlertAsync(
        InstantViolationAlertPayload payload,
        CancellationToken cancellationToken = default)
    {
        var normalizedLabel = payload.Label?.Trim() ?? string.Empty;
        var detectedAtUtc = payload.DetectedAtUtc == default ? DateTime.UtcNow : payload.DetectedAtUtc;
        var ruleType = payload.RuleType?.Trim() ?? string.Empty;
        var isSmoke = string.Equals(ruleType, "smoke", StringComparison.OrdinalIgnoreCase)
            || SmokeLabels.Contains(normalizedLabel);
        var isLeaving = string.Equals(ruleType, "empty_desk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleType, "leave", StringComparison.OrdinalIgnoreCase)
            || EmptyChairLabels.Contains(normalizedLabel);

        if (!isSmoke && !isLeaving)
        {
            throw new InvalidOperationException($"Không hỗ trợ rule instant alert '{payload.RuleType}' với nhãn '{payload.Label}'.");
        }

        var evidenceUrl = SaveEvidenceImage(
            payload.SnapshotBase64,
            payload.SnapshotMimeType,
            string.IsNullOrWhiteSpace(payload.ModelType) ? (isSmoke ? "YoloSmoking" : "YoloLeaving") : payload.ModelType,
            detectedAtUtc) ?? (isSmoke ? "/evidence/monitoring-smoke.jpg" : "/evidence/monitoring-leave.jpg");

        var trackId = string.IsNullOrWhiteSpace(payload.TrackId)
            ? BuildTrackId(isSmoke)
            : payload.TrackId;
        var durationSeconds = Math.Round(Math.Max(0d, payload.DurationSeconds), 1);
        var cameraLocation = VietnameseText.NormalizeMojibake(string.IsNullOrWhiteSpace(payload.CameraLocation) ? _options.CameraLocation : payload.CameraLocation);
        var sourceLabel = string.IsNullOrWhiteSpace(payload.SourceLabel) ? payload.SourceType : payload.SourceLabel;

        var alert = isSmoke
            ? new PendingAlert(
                trackId,
                "Hút thuốc tại khu vực làm việc",
                "High",
                detectedAtUtc,
                evidenceUrl,
                $"[CẢNH BÁO HÚT THUỐC - VỪA PHÁT HIỆN]\nTrack ID: {trackId}\nNhãn phát hiện: {normalizedLabel}\nThời gian duy trì: {durationSeconds:0.#} giây\nCamera: {cameraLocation}\nNguồn: {sourceLabel}\nThời điểm phát hiện: {detectedAtUtc:dd/MM/yyyy HH:mm:ss}\nẢnh vi phạm được gửi kèm ngay bên dưới.")
            : new PendingAlert(
                trackId,
                "Rời vị trí làm việc",
                "Medium",
                detectedAtUtc,
                evidenceUrl,
                $"[CẢNH BÁO RỜI VỊ TRÍ - VỪA PHÁT HIỆN]\nTrack ID: {trackId}\nNhãn phát hiện: {normalizedLabel}\nGhế trống kéo dài: {durationSeconds:0.#} giây\nCamera: {cameraLocation}\nNguồn: {sourceLabel}\nThời điểm phát hiện: {detectedAtUtc:dd/MM/yyyy HH:mm:ss}\nẢnh vi phạm được gửi kèm ngay bên dưới.");

        var result = await PublishSingleManualAlertAsync(alert, cancellationToken);
        result.TelegramAttempted = true;
        return result;
    }

    private async Task<IReadOnlyCollection<ViolationAlertResult>> ProcessDetectionsInternalAsync(
        IReadOnlyCollection<DetectionResult> detections,
        IReadOnlyDictionary<string, string> evidenceByModelType,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        List<PendingAlert> alertsToPublish;

        lock (_syncLock)
        {
            var effectiveRuns = detections
                .GroupBy(detection => detection.ModelType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new YoloInferenceRunResult
                {
                    ModelType = group.Key,
                    Detections = group.ToList()
                })
                .ToList();

            alertsToPublish = [];
            ApplyTrackingToRuns(
                effectiveRuns,
                _smokeTracks,
                nowUtc,
                isSmokeTrack: true,
                evidenceUrl: evidenceByModelType.GetValueOrDefault("YoloSmoking"),
                alertsToPublish);
            ApplyTrackingToRuns(
                effectiveRuns,
                _emptyChairTracks,
                nowUtc,
                isSmokeTrack: false,
                evidenceUrl: evidenceByModelType.GetValueOrDefault("YoloLeaving"),
                alertsToPublish);
            PruneStaleTracks(_smokeTracks, nowUtc);
            PruneStaleTracks(_emptyChairTracks, nowUtc);
        }

        return await PublishAlertsAsync(alertsToPublish, cancellationToken);
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
        var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs.InternalChatHub>>();
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
                CameraLocation = VietnameseText.NormalizeMojibake(_options.CameraLocation),
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

            // Gửi thông báo đến Manager's menudrop
            dbContext.EmployeeMessages.Add(new EmployeeMessage
            {
                EmployeeUserId = Guid.Empty,
                EmployeeUsername = "System",
                EmployeeName = "Hệ thống giám sát",
                Channel = "violations",
                SenderRole = "System",
                SenderName = "Hệ thống giám sát",
                Title = "Phát hiện vi phạm mới",
                Content = $"Hệ thống vừa phát hiện vi phạm: {alert.ViolationType} (TrackID: {alert.TrackId}). Vui lòng vào mục Giám sát để xem chi tiết.",
                SentAt = alert.DetectedAtUtc,
                IsRead = false
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
                var telegramResults = await telegramService.SendViolationAlertAsync(violation, result.Message, cancellationToken);
                violation.TelegramSent = telegramResults.Any(item => item.Success);
                violation.TelegramPhotoSent = telegramResults.Any(item => item.PhotoSent);
                violation.TelegramSentAtUtc = violation.TelegramSent ? DateTime.UtcNow : null;
                violation.TelegramDeliveryMode = string.Join(", ", telegramResults
                    .Select(item => item.DeliveryMode)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal));
                violation.TelegramTargetChatIds = string.Join(", ", telegramResults.Select(item => item.ChatId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal));
                violation.TelegramLastError = telegramResults.FirstOrDefault(item => !item.Success)?.ResponseSummary;
                await dbContext.SaveChangesAsync(cancellationToken);
                result.TelegramAttempted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Telegram alert for {TrackId}", result.TrackId);
            }

            try
            {
                await hubContext.Clients.Group(Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs.InternalChatHub.BuildRoleGroup("Manager")).SendAsync("ReceiveNotification", $"Hệ thống Giám sát AI vừa phát hiện 1 vi phạm mới ({result.ViolationType}). Nhấn để xem chi tiết.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR notification to Manager for {TrackId}", result.TrackId);
            }
        }

        return results;
    }

    private void ApplyTrackingToRuns(
        IReadOnlyCollection<YoloInferenceRunResult> runs,
        Dictionary<string, TrackedDetection> tracks,
        DateTime nowUtc,
        bool isSmokeTrack,
        string? evidenceUrl,
        List<PendingAlert>? alertsToPublish)
    {
        var targetModelType = isSmokeTrack ? "YoloSmoking" : "YoloLeaving";
        var targetLabels = isSmokeTrack ? SmokeLabels : EmptyChairLabels;
        var targetRuns = runs
            .Where(run => string.Equals(run.ModelType, targetModelType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchedTrackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in targetRuns)
        {
            foreach (var detection in run.Detections.Where(detection => targetLabels.Contains(detection.Label)))
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
                        TrackId = BuildTrackId(isSmokeTrack),
                        BoundingBox = box,
                        FirstSeenUtc = nowUtc,
                        LastSeenUtc = nowUtc,
                        SeenCount = 1,
                        Label = detection.Label,
                        EvidenceUrl = evidenceUrl ?? string.Empty
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
                    if (!string.IsNullOrWhiteSpace(evidenceUrl))
                    {
                        track.EvidenceUrl = evidenceUrl;
                    }
                }

                detection.TrackId = track.TrackId;
                matchedTrackIds.Add(track.TrackId);

                if (alertsToPublish == null || track.AlertRaised)
                {
                    continue;
                }

                if (isSmokeTrack && track.SeenCount > _options.SmokeDetectionThresholdCount)
                {
                    track.AlertRaised = true;
                    alertsToPublish.Add(BuildSmokeAlert(track, nowUtc));
                }

                if (!isSmokeTrack && nowUtc - track.FirstSeenUtc >= _options.GetEmptyChairThreshold())
                {
                    track.AlertRaised = true;
                    alertsToPublish.Add(BuildLeavingAlert(track, nowUtc));
                }
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
            string.IsNullOrWhiteSpace(track.EvidenceUrl) ? "/evidence/monitoring-smoke.jpg" : track.EvidenceUrl,
            $"[CẢNH BÁO HÚT THUỐC - VỪA PHÁT HIỆN]\nTrack ID: {track.TrackId}\nNhãn phát hiện: {track.Label}\nSố lần phát hiện thuốc lá: {track.SeenCount}\nCamera: {VietnameseText.NormalizeMojibake(_options.CameraLocation)}\nThời điểm phát hiện: {nowUtc:dd/MM/yyyy HH:mm:ss}\nẢnh vi phạm được gửi kèm ngay bên dưới.");
    }

    private PendingAlert BuildLeavingAlert(TrackedDetection track, DateTime nowUtc)
    {
        var seconds = Math.Round((nowUtc - track.FirstSeenUtc).TotalSeconds, 1);
        return new PendingAlert(
            track.TrackId,
            "Rời vị trí làm việc",
            "Medium",
            nowUtc,
            string.IsNullOrWhiteSpace(track.EvidenceUrl) ? "/evidence/monitoring-leave.jpg" : track.EvidenceUrl,
            $"[CẢNH BÁO RỜI VỊ TRÍ - VỪA PHÁT HIỆN]\nTrack ID: {track.TrackId}\nNhãn phát hiện: {track.Label}\nGhế trống / non-human kéo dài: {seconds:0.#} giây\nCamera: {VietnameseText.NormalizeMojibake(_options.CameraLocation)}\nThời điểm phát hiện: {nowUtc:dd/MM/yyyy HH:mm:ss}\nẢnh vi phạm được gửi kèm ngay bên dưới.");
    }

    private string? SaveEvidenceImage(string annotatedBase64, string mimeType, string modelType, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(annotatedBase64))
        {
            return null;
        }

        try
        {
            var evidenceDirectory = Path.Combine(_environment.WebRootPath, "evidence", "monitoring");
            Directory.CreateDirectory(evidenceDirectory);

            var extension = string.Equals(mimeType, "image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var fileName = $"{modelType.ToLowerInvariant()}_{nowUtc:yyyyMMdd_HHmmss_fff}{extension}";
            var absolutePath = Path.Combine(evidenceDirectory, fileName);
            File.WriteAllBytes(absolutePath, Convert.FromBase64String(annotatedBase64));

            return $"/evidence/monitoring/{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể lưu ảnh evidence cho model {ModelType}", modelType);
            return null;
        }
    }

    private static string BuildTrackId(bool isSmokeTrack)
    {
        var prefix = isSmokeTrack ? "PER-SMK" : "PER-LVE";
        return $"{prefix}-{Guid.NewGuid():N}"[..18];
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
        public string EvidenceUrl { get; set; } = string.Empty;
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
