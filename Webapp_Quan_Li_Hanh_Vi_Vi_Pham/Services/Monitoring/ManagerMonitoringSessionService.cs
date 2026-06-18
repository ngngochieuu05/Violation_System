using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ManagerMonitoringSessionService : IManagerMonitoringSessionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ManagerMonitoringSessionService> _logger;
    private readonly YoloModelOptions _options;
    private readonly ViolationMonitoringOptions _monitoringOptions;
    private readonly ConcurrentDictionary<string, SessionHandle> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ManagerMonitoringSessionService(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment environment,
        IOptions<YoloModelOptions> options,
        IOptions<ViolationMonitoringOptions> monitoringOptions,
        ILogger<ManagerMonitoringSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _options = options.Value;
        _monitoringOptions = monitoringOptions.Value;
        _logger = logger;
    }

    public async Task<ManagerMonitoringSessionStartResult> StartSessionAsync(
        string ownerKey,
        ManagerMonitoringSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        await StopSessionAsync(ownerKey, cancellationToken: cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();
        var models = await dbContext.AiModels
            .Where(model => model.IsActive
                && (model.Type.StartsWith("Yolo")
                    || model.ModelPath.EndsWith(".pt")
                    || model.ModelPath.EndsWith(".onnx")))
            .OrderBy(model => model.Type)
            .ToListAsync(cancellationToken);

        if (models.Count == 0)
        {
            return new ManagerMonitoringSessionStartResult
            {
                Success = false,
                Message = "Chưa có model YOLO active để khởi chạy giám sát."
            };
        }

        var sessionId = $"{SanitizeKey(ownerKey)}_{Guid.NewGuid():N}";
        var sessionDirectory = Path.Combine(_environment.WebRootPath, "evidence", "monitoring", "sessions", sessionId);
        Directory.CreateDirectory(sessionDirectory);

        var sourcePath = ResolveSourcePath(request);
        var modelsConfigPath = Path.Combine(sessionDirectory, "models.json");
        var modelsConfig = models.Select(model => new
        {
            model.Name,
            model.Type,
            ModelPath = ResolveModelPath(model),
            model.ConfThreshold,
            model.IouThreshold
        });
        await File.WriteAllTextAsync(
            modelsConfigPath,
            JsonSerializer.Serialize(modelsConfig),
            Encoding.UTF8,
            cancellationToken);

        var pythonExe = ResolvePythonExecutable();
        var scriptPath = ResolvePath("ML/scripts/yolo_monitor_session.py");
        var args =
            $"\"{scriptPath}\" --session-dir \"{sessionDirectory}\" --models-config \"{modelsConfigPath}\" --source \"{sourcePath}\" --source-type \"{request.SourceType}\" --device \"{_options.DeviceMode}\" --half {(_options.UseHalfPrecision ? "1" : "0")} --imgsz {_options.ImageSize} --fps 6 --callback-url \"{_monitoringOptions.InstantAlertEndpointUrl}\" --callback-key \"{_monitoringOptions.InstantAlertApiKey}\" --camera-location \"{_monitoringOptions.CameraLocation}\" --person-label \"{_monitoringOptions.PersonLabel}\" --smoke-label \"{_monitoringOptions.SmokeLabel}\" --empty-desk-label \"{_monitoringOptions.EmptyDeskLabel}\" --smoke-seconds {_monitoringOptions.SmokeAlertSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} --empty-desk-seconds {_monitoringOptions.EmptyDeskAlertSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = args,
                WorkingDirectory = _environment.ContentRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                LogWorkerMessage(sessionId, eventArgs.Data, isError: false);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                LogWorkerMessage(sessionId, eventArgs.Data, isError: true);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var handle = new SessionHandle(
            ownerKey,
            sessionId,
            request.SourceType,
            request.SourceType.Equals("webcam", StringComparison.OrdinalIgnoreCase)
                ? $"Camera {request.CameraIndex}"
                : Path.GetFileName(sourcePath),
            sessionDirectory,
            process);

        _sessions[ownerKey] = handle;

        return new ManagerMonitoringSessionStartResult
        {
            Success = true,
            SessionId = sessionId,
            SourceType = request.SourceType,
            Message = "Đã khởi tạo phiên giám sát. Website sẽ tự đọc frame và detections mới nhất."
        };
    }

    public async Task<ManagerMonitoringSessionStatus?> GetSessionStatusAsync(
        string ownerKey,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(ownerKey, out var handle))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(sessionId) && !string.Equals(handle.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var statusPath = Path.Combine(handle.SessionDirectory, "status.json");
        if (!File.Exists(statusPath))
        {
            return new ManagerMonitoringSessionStatus
            {
                Success = true,
                SessionId = handle.SessionId,
                State = handle.Process.HasExited ? "stopped" : "starting",
                Message = handle.Process.HasExited ? "Phiên giám sát đã dừng." : "Đang khởi tạo worker và load model...",
                SourceType = handle.SourceType,
                SourceLabel = handle.SourceLabel
            };
        }

        await using var stream = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var snapshot = await JsonSerializer.DeserializeAsync<WorkerStatusSnapshot>(stream, cancellationToken: cancellationToken);
        if (snapshot == null)
        {
            return null;
        }

        return new ManagerMonitoringSessionStatus
        {
            Success = true,
            SessionId = handle.SessionId,
            State = snapshot.State ?? "running",
            Message = snapshot.Message ?? string.Empty,
            SourceType = handle.SourceType,
            SourceLabel = snapshot.SourceLabel ?? handle.SourceLabel,
            GpuAvailable = snapshot.GpuAvailable,
            DeviceResolved = snapshot.DeviceResolved ?? string.Empty,
            FrameIndex = snapshot.FrameIndex,
            UpdatedAtUnixMs = snapshot.UpdatedAtUnixMs,
            PrimaryModelType = snapshot.PrimaryModelType,
            Models = snapshot.Models.Select(model => new ManagerMonitoringModelOutput
            {
                ModelType = model.ModelType ?? string.Empty,
                ModelName = model.ModelName ?? string.Empty,
                ModelPath = model.ModelPath ?? string.Empty,
                ConfThreshold = model.ConfThreshold,
                IouThreshold = model.IouThreshold,
                IsMockResult = model.IsMockResult,
                ElapsedMilliseconds = model.ElapsedMilliseconds,
                ImageUrl = string.IsNullOrWhiteSpace(model.ImageFileName)
                    ? null
                    : $"/evidence/monitoring/sessions/{handle.SessionId}/{model.ImageFileName}",
                DetectionCount = model.DetectionCount,
                Detections = model.Detections.Select(detection => new ManagerMonitoringDetectionItem
                {
                    Label = detection.Label ?? string.Empty,
                    DisplayLabel = detection.DisplayLabel ?? string.Empty,
                    Confidence = detection.Confidence,
                    BoundingBox = detection.BoundingBox ?? string.Empty,
                    TrackId = detection.TrackId,
                    ProcessedAtUtc = detection.ProcessedAtUtc
                }).ToList()
            }).ToList()
        };
    }

    public Task StopSessionAsync(
        string ownerKey,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryRemove(ownerKey, out var handle))
        {
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(sessionId) && !string.Equals(handle.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            _sessions[ownerKey] = handle;
            return Task.CompletedTask;
        }

        try
        {
            if (!handle.Process.HasExited)
            {
                handle.Process.Kill(entireProcessTree: true);
                handle.Process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể dừng monitoring session {SessionId}", handle.SessionId);
        }

        return Task.CompletedTask;
    }

    private string ResolvePythonExecutable()
    {
        var venvExe = Path.Combine(_environment.ContentRootPath, ".venv", "Scripts", "python.exe");
        return File.Exists(venvExe) ? venvExe : _options.PythonExecutable;
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_environment.ContentRootPath, relativeOrAbsolutePath);
    }

    private string ResolveSourcePath(ManagerMonitoringSessionRequest request)
    {
        if (string.Equals(request.SourceType, "webcam", StringComparison.OrdinalIgnoreCase))
        {
            return $"camera:{Math.Max(0, request.CameraIndex)}";
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("Nguồn file không hợp lệ cho phiên giám sát.");
        }

        return request.FilePath;
    }

    private string ResolveModelPath(AiModel model)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.ModelPath))
        {
            candidates.Add(model.ModelPath);
            candidates.Add(model.ModelPath.Replace("/", "\\", StringComparison.Ordinal));
        }

        var knownFallback = model.Type switch
        {
            "YoloSmoking" => @"D:\WEB\model_trained\ML\Model_trained\smoke_v1_yolov8\train_yolov8n_200ep\weights\best.pt",
            "YoloLeaving" => @"D:\WEB\model_trained\ML\Model_trained\rc_v1_yolov8\train_yolov8n_200ep\weights\best.pt",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(knownFallback))
        {
            candidates.Add(knownFallback);
        }

        foreach (var candidate in candidates)
        {
            var normalized = NormalizePotentialWindowsPath(candidate);
            var absolutePath = Path.IsPathRooted(normalized) ? normalized : ResolvePath(normalized);
            if (File.Exists(absolutePath))
            {
                return absolutePath;
            }
        }

        return model.ModelPath;
    }

    private static string NormalizePotentialWindowsPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var cleaned = rawPath.Trim();

        // Trường hợp path bị mất backslash sau drive letter (ví dụ: D:WEB thay vì D:\WEB)
        // Regex: nếu ký tự thứ 2 là ':' nhưng ký tự thứ 3 KHÔNG phải '\', thêm '\' vào
        if (cleaned.Length >= 3 && cleaned[1] == ':' && cleaned[2] != '\\')
        {
            cleaned = cleaned.Insert(2, "\\");
        }

        return cleaned;
    }

    private static string SanitizeKey(string ownerKey)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = ownerKey
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray();
        return new string(sanitizedChars);
    }

    private sealed record SessionHandle(
        string OwnerKey,
        string SessionId,
        string SourceType,
        string SourceLabel,
        string SessionDirectory,
        Process Process);

    private void LogWorkerMessage(string sessionId, string rawMessage, bool isError)
    {
        var message = rawMessage.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(type, "instant-alert", StringComparison.OrdinalIgnoreCase))
                {
                    var ruleType = root.TryGetProperty("ruleType", out var ruleElement) ? ruleElement.GetString() : "unknown";
                    var trackId = root.TryGetProperty("trackId", out var trackElement) ? trackElement.GetString() : "n/a";
                    var duration = root.TryGetProperty("durationSeconds", out var durationElement) ? durationElement.ToString() : "0";
                    _logger.LogInformation(
                        "[monitoring-session:{SessionId}] alert {RuleType} track={TrackId} duration={Duration}s",
                        sessionId,
                        ruleType,
                        trackId,
                        duration);
                    return;
                }

                if (string.Equals(type, "instant-alert-error", StringComparison.OrdinalIgnoreCase))
                {
                    var ruleType = root.TryGetProperty("ruleType", out var ruleElement) ? ruleElement.GetString() : "unknown";
                    var trackId = root.TryGetProperty("trackId", out var trackElement) ? trackElement.GetString() : "n/a";
                    var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : message;
                    _logger.LogWarning(
                        "[monitoring-session:{SessionId}] alert-error {RuleType} track={TrackId}: {Error}",
                        sessionId,
                        ruleType,
                        trackId,
                        error);
                    return;
                }

                // Ignore high-frequency JSON noise from the worker. The UI reads progress from status.json.
                return;
            }
            catch (JsonException)
            {
                // Fall through for plain-text logging.
            }
        }

        if (isError)
        {
            _logger.LogWarning("[monitoring-session:{SessionId}] {Message}", sessionId, message);
            return;
        }

        _logger.LogDebug("[monitoring-session:{SessionId}] {Message}", sessionId, message);
    }

    private sealed class WorkerStatusSnapshot
    {
        public string? State { get; set; }
        public string? Message { get; set; }
        public string? SourceLabel { get; set; }
        public bool GpuAvailable { get; set; }
        public string? DeviceResolved { get; set; }
        public int FrameIndex { get; set; }
        public long UpdatedAtUnixMs { get; set; }
        public string? PrimaryModelType { get; set; }
        public List<WorkerModelSnapshot> Models { get; set; } = [];
    }

    private sealed class WorkerModelSnapshot
    {
        public string? ModelType { get; set; }
        public string? ModelName { get; set; }
        public string? ModelPath { get; set; }
        public decimal ConfThreshold { get; set; }
        public decimal IouThreshold { get; set; }
        public bool IsMockResult { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string? ImageFileName { get; set; }
        public int DetectionCount { get; set; }
        public List<WorkerDetectionSnapshot> Detections { get; set; } = [];
    }

    private sealed class WorkerDetectionSnapshot
    {
        public string? Label { get; set; }
        public string? DisplayLabel { get; set; }
        public decimal Confidence { get; set; }
        public string? BoundingBox { get; set; }
        public string? TrackId { get; set; }
        public DateTime ProcessedAtUtc { get; set; }
    }
}
