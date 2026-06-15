using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class LocalYoloInferenceService : IYoloInferenceService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalYoloInferenceService> _logger;
    private readonly YoloModelOptions _options;
    private readonly ViolationDbContext _context;
    private readonly YoloPythonWorkerClient _workerClient;

    public LocalYoloInferenceService(
        IWebHostEnvironment environment,
        IOptions<YoloModelOptions> options,
        ILogger<LocalYoloInferenceService> logger,
        ViolationDbContext context,
        YoloPythonWorkerClient workerClient)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value;
        _context = context;
        _workerClient = workerClient;
    }

    public async Task<IReadOnlyCollection<DetectionResult>> GetLatestDetectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var runs = await RunInferenceAsync(cancellationToken: cancellationToken);
        return runs.SelectMany(run => run.Detections).ToList();
    }

    public async Task<IReadOnlyCollection<YoloInferenceRunResult>> RunInferenceAsync(
        string? sourcePath = null,
        IEnumerable<string>? requestedModelTypes = null,
        int? maxFrames = null,
        decimal? confThreshold = null,
        decimal? iouThreshold = null,
        string? deviceMode = null,
        int? imageSize = null,
        bool? useHalfPrecision = null,
        CancellationToken cancellationToken = default)
    {
        var activeModels = await _context.AiModels
            .Where(m => m.IsActive
                && (m.Type.StartsWith("Yolo")
                    || m.ModelPath.EndsWith(".pt")
                    || m.ModelPath.EndsWith(".onnx")))
            .OrderBy(m => m.Type)
            .ToListAsync(cancellationToken);

        if (requestedModelTypes is not null)
        {
            var requestedSet = requestedModelTypes
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requestedSet.Count > 0)
            {
                activeModels = activeModels
                    .Where(model => requestedSet.Contains(model.Type))
                    .ToList();
            }
        }

        var targetModels = activeModels.Count > 0
            ? activeModels
            :
            [
                new AiModel
                {
                    Name = "Fallback YOLO Smoking Detection",
                    Type = "YoloSmoking",
                    ModelPath = _options.ModelPath,
                    ConfThreshold = 0.25m,
                    IouThreshold = 0.45m,
                    IsActive = true
                }
            ];

        var effectiveSourcePath = ResolveInferenceSourcePath(sourcePath);
        var runtimeSource = activeModels.Count > 0 ? "AiModels" : "appsettings fallback";

        var tasks = targetModels
            .Select(model => RunSingleModelInferenceAsync(
                model,
                effectiveSourcePath,
                runtimeSource,
                maxFrames,
                confThreshold,
                iouThreshold,
                deviceMode,
                imageSize,
                useHalfPrecision,
                cancellationToken))
            .ToArray();

        var runs = await Task.WhenAll(tasks);
        return runs
            .OrderBy(run => run.ModelType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<YoloInferenceRunResult> RunSingleModelInferenceAsync(
        AiModel model,
        string effectiveSourcePath,
        string runtimeSource,
        int? maxFrames,
        decimal? confOverride,
        decimal? iouOverride,
        string? deviceModeOverride,
        int? imageSizeOverride,
        bool? useHalfPrecisionOverride,
        CancellationToken cancellationToken)
    {
        var modelPath = ResolveModelPath(model);
        var run = new YoloInferenceRunResult
        {
            ModelName = model.Name,
            ModelType = model.Type,
            ModelPath = modelPath,
            ModelFormat = Path.GetExtension(modelPath).ToLowerInvariant(),
            ConfThreshold = NormalizeThreshold(confOverride ?? model.ConfThreshold, fallback: 0.25m),
            IouThreshold = NormalizeThreshold(iouOverride ?? model.IouThreshold, fallback: 0.45m),
            SourcePath = effectiveSourcePath,
            RuntimeSource = runtimeSource,
            Engine = Path.GetExtension(modelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
                ? "ultralytics + onnxruntime"
                : "ultralytics"
        };

        if (!File.Exists(modelPath) || (!IsCameraSource(effectiveSourcePath) && !File.Exists(effectiveSourcePath)))
        {
            _logger.LogInformation(
                "YOLO local files are not ready for {ModelType} (model: {ModelExists}, sourceReady: {SourceReady}). Returning seeded detections.",
                model.Type,
                File.Exists(modelPath),
                IsCameraSource(effectiveSourcePath) || File.Exists(effectiveSourcePath));
            return BuildMockRun(run, model.Name);
        }

        try
        {
            var payload = await _workerClient.RunAsync(
                new YoloWorkerRequest
                {
                    ModelPath = modelPath,
                    SourcePath = effectiveSourcePath,
                    ModelType = model.Type,
                    Label = model.Name ?? string.Empty,
                    ConfThreshold = run.ConfThreshold,
                    IouThreshold = run.IouThreshold,
                    DeviceMode = string.IsNullOrWhiteSpace(deviceModeOverride) ? _options.DeviceMode : deviceModeOverride,
                    UseHalfPrecision = useHalfPrecisionOverride ?? _options.UseHalfPrecision,
                    MaxFrames = Math.Max(1, maxFrames ?? _options.MaxFrames),
                    ImageSize = Math.Max(320, imageSizeOverride ?? _options.ImageSize)
                },
                cancellationToken);

            var modelDetections = payload.Detections ?? [];
            foreach (var detection in modelDetections)
            {
                if (string.IsNullOrWhiteSpace(detection.ModelType))
                {
                    detection.ModelType = model.Type;
                }

                if (string.IsNullOrWhiteSpace(detection.DisplayLabel))
                {
                    detection.DisplayLabel = string.IsNullOrWhiteSpace(model.Name)
                        ? detection.Label
                        : $"{model.Name}: {detection.Label}";
                }
            }

            run.IsMockResult = payload.IsMock;
            run.StatusMessage = BuildStatusMessage(payload, effectiveSourcePath);
            run.AnnotatedImageBase64 = payload.AnnotatedBase64;
            run.AnnotatedImageMimeType = payload.ImageMimeType ?? "image/jpeg";
            run.FrameIndex = payload.FrameIndex;
            run.FramesExamined = payload.FramesExamined;
            run.ElapsedMilliseconds = payload.ElapsedMs;
            run.Detections = modelDetections;
            run.Engine = BuildEngine(run.ModelFormat, payload);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YOLO worker inference failed for {ModelType}", model.Type);
            run.StatusMessage = $"Worker Python lỗi: {ex.Message}. Hệ thống tạm trả dữ liệu mô phỏng.";
            return BuildMockRun(run, model.Name);
        }
    }

    private static YoloInferenceRunResult BuildMockRun(YoloInferenceRunResult run, string? modelName)
    {
        run.IsMockResult = true;
        if (string.IsNullOrWhiteSpace(run.StatusMessage))
        {
            run.StatusMessage = "Thiếu model hoặc nguồn video nên hệ thống trả về dữ liệu mô phỏng.";
        }

        run.Detections = GetSeedDetections(run.ModelType).ToList();
        run.AnnotatedImageBase64 = GenerateAnnotatedPreview(run.Detections, modelName ?? string.Empty, run.ModelType);
        run.AnnotatedImageMimeType = "image/svg+xml";
        return run;
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_environment.ContentRootPath, relativeOrAbsolutePath);
    }

    private string ResolveInferenceSourcePath(string? preferredSourcePath)
    {
        if (IsCameraSource(preferredSourcePath))
        {
            return preferredSourcePath!;
        }

        if (!string.IsNullOrWhiteSpace(preferredSourcePath))
        {
            var explicitPath = ResolvePath(preferredSourcePath);
            if (File.Exists(explicitPath))
            {
                return explicitPath;
            }
        }

        var configuredSourcePath = ResolvePath(_options.SampleSourcePath);
        if (File.Exists(configuredSourcePath))
        {
            return configuredSourcePath;
        }

        var managerTestsDirectory = Path.Combine(_environment.ContentRootPath, "ML", "samples", "manager-tests");
        if (Directory.Exists(managerTestsDirectory))
        {
            var latestTestVideo = new DirectoryInfo(managerTestsDirectory)
                .GetFiles("*.*", SearchOption.TopDirectoryOnly)
                .Where(file => file.Extension is ".mp4" or ".avi" or ".mov" or ".mkv" or ".webm")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latestTestVideo is not null)
            {
                return latestTestVideo.FullName;
            }
        }

        return configuredSourcePath;
    }

    private static bool IsCameraSource(string? preferredSourcePath)
    {
        return !string.IsNullOrWhiteSpace(preferredSourcePath)
            && preferredSourcePath.StartsWith("camera:", StringComparison.OrdinalIgnoreCase);
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

        return ResolvePath(model.ModelPath);
    }

    private static string NormalizePotentialWindowsPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var cleaned = rawPath.Trim();

        if (cleaned.Length >= 3 && cleaned[1] == ':' && cleaned[2] != '\\')
        {
            cleaned = cleaned.Insert(2, "\\");
        }

        return cleaned;
    }

    private static decimal NormalizeThreshold(decimal value, decimal fallback)
    {
        var normalized = value;
        if (normalized <= 0m)
        {
            normalized = fallback;
        }
        else if (normalized > 1m)
        {
            normalized /= 100m;
        }

        normalized = Math.Clamp(normalized, 0.05m, 1.00m);
        return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyCollection<DetectionResult> GetSeedDetections(string modelType)
    {
        return modelType switch
        {
            "YoloSmoking" =>
            [
                new DetectionResult
                {
                    ModelType = "YoloSmoking",
                    Label = "Cigarette",
                    DisplayLabel = "Phát hiện hút thuốc",
                    Confidence = 0.94m,
                    BoundingBox = "x:132,y:46,w:88,h:90",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            ],
            "YoloLeaving" =>
            [
                new DetectionResult
                {
                    ModelType = "YoloLeaving",
                    Label = "un-occupied_desk",
                    DisplayLabel = "Ghế trống / không có người",
                    Confidence = 0.91m,
                    BoundingBox = "x:218,y:74,w:112,h:180",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            ],
            _ =>
            [
                new DetectionResult
                {
                    ModelType = modelType,
                    Label = "unknown",
                    DisplayLabel = "Không xác định",
                    Confidence = 0.50m,
                    BoundingBox = "x:120,y:40,w:84,h:92",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            ]
        };
    }

    private static string GenerateAnnotatedPreview(
        IReadOnlyCollection<DetectionResult> detections,
        string modelName,
        string modelType)
    {
        const int canvasWidth = 640;
        const int canvasHeight = 360;
        var stroke = modelType.Equals("YoloSmoking", StringComparison.OrdinalIgnoreCase) ? "#f97316" : "#10b981";
        var background = "#121212";
        var builder = new StringBuilder();

        builder.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{canvasWidth}' height='{canvasHeight}' viewBox='0 0 {canvasWidth} {canvasHeight}'>");
        builder.Append($"<rect width='{canvasWidth}' height='{canvasHeight}' fill='{background}' />");
        builder.Append("<text x='16' y='30' fill='#ffffff' font-size='22' font-family='Segoe UI, Arial, sans-serif' font-weight='700'>ComplianceHub Monitoring</text>");
        builder.Append($"<text x='16' y='56' fill='{stroke}' font-size='15' font-family='Segoe UI, Arial, sans-serif'>Mô hình: {EscapeSvg(modelName)}</text>");

        foreach (var detection in detections)
        {
            var rect = ParseBoundingRectangle(detection.BoundingBox);
            var caption = string.IsNullOrWhiteSpace(detection.DisplayLabel)
                ? detection.Label
                : detection.DisplayLabel;
            var renderedCaption = string.IsNullOrWhiteSpace(modelName) ? caption : $"{modelName}: {caption}";
            var labelY = Math.Max(24, rect.Y - 8);

            builder.Append($"<rect x='{rect.X}' y='{rect.Y}' width='{rect.Width}' height='{rect.Height}' fill='none' stroke='{stroke}' stroke-width='3' rx='8' />");
            builder.Append($"<text x='{rect.X}' y='{labelY}' fill='{stroke}' font-size='14' font-family='Segoe UI, Arial, sans-serif' font-weight='700'>{EscapeSvg(renderedCaption)} {detection.Confidence:0.00}</text>");
        }

        if (detections.Count == 0)
        {
            builder.Append("<text x='16' y='96' fill='#d1d5db' font-size='15' font-family='Segoe UI, Arial, sans-serif'>Không phát hiện đối tượng phù hợp trong ảnh mẫu.</text>");
        }

        builder.Append("</svg>");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static BoundingRectangle ParseBoundingRectangle(string raw)
    {
        var values = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(part => part.Length == 2)
            .ToDictionary(
                part => part[0],
                part => int.TryParse(part[1], out var value) ? value : 0,
                StringComparer.OrdinalIgnoreCase);

        return new BoundingRectangle(
            values.GetValueOrDefault("x"),
            values.GetValueOrDefault("y"),
            Math.Max(24, values.GetValueOrDefault("w")),
            Math.Max(24, values.GetValueOrDefault("h")));
    }

    private static string EscapeSvg(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string BuildStatusMessage(YoloWorkerResponse payload, string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(payload.ErrorMessage))
        {
            return payload.ErrorMessage;
        }

        var runtimeMode = payload.ModelLoadedFromCache ? "cache model" : "model mới";
        var sourceFileName = Path.GetFileName(sourcePath);
        return payload.IsMock
            ? $"Worker đã chạy nhưng đang trả dữ liệu mô phỏng từ {sourceFileName}."
            : $"Đã chạy model thật trên {sourceFileName} bằng {payload.DeviceResolved ?? "cpu"} ({runtimeMode}, load {payload.ModelLoadMs} ms).";
    }

    private static string BuildEngine(string modelFormat, YoloWorkerResponse payload)
    {
        var baseEngine = modelFormat.Equals(".onnx", StringComparison.OrdinalIgnoreCase)
            ? "ultralytics + onnxruntime"
            : "ultralytics";
        return $"{baseEngine} | device {payload.DeviceResolved ?? "cpu"}";
    }

    private readonly record struct BoundingRectangle(int X, int Y, int Width, int Height);
}
