using System.Diagnostics;
using System.Text.Json;
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

    public LocalYoloInferenceService(
        IWebHostEnvironment environment,
        IOptions<YoloModelOptions> options,
        ILogger<LocalYoloInferenceService> logger,
        ViolationDbContext context)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value;
        _context = context;
    }

    public async Task<IReadOnlyCollection<DetectionResult>> GetLatestDetectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var activeModels = await _context.AiModels
            .Where(m => m.Type.StartsWith("Yolo") && m.IsActive)
            .OrderBy(m => m.Type)
            .ToListAsync(cancellationToken);

        var targetModels = activeModels.Count > 0
            ? activeModels
            : [new AiModel
            {
                Name = "Fallback YOLO Smoking Detection",
                Type = "YoloSmoking",
                ModelPath = _options.ModelPath,
                ConfThreshold = 0.25m,
                IouThreshold = 0.45m,
                IsActive = true
            }];

        var scriptPath = ResolvePath(_options.InferenceScriptPath);
        var sampleSourcePath = ResolvePath(_options.SampleSourcePath);

        var pythonExe = _options.PythonExecutable;
        var venvExe = Path.Combine(_environment.ContentRootPath, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvExe))
        {
            pythonExe = venvExe;
        }

        var detections = new List<DetectionResult>();
        foreach (var model in targetModels)
        {
            var modelPath = ResolvePath(model.ModelPath);
            if (!File.Exists(scriptPath) || !File.Exists(modelPath) || !File.Exists(sampleSourcePath))
            {
                _logger.LogInformation(
                    "YOLO local files are not ready for {ModelType} (script: {ScriptExists}, model: {ModelExists}, sample: {SampleExists}). Returning seeded detections.",
                    model.Type,
                    File.Exists(scriptPath),
                    File.Exists(modelPath),
                    File.Exists(sampleSourcePath));
                detections.AddRange(GetSeedDetections(model.Type));
                continue;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --model \"{modelPath}\" --source \"{sampleSourcePath}\" --label \"{model.Name ?? ""}\" --conf {model.ConfThreshold} --iou {model.IouThreshold} --model-type \"{model.Type}\"",
                WorkingDirectory = _environment.ContentRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "YOLO local inference failed for {ModelType} with exit code {ExitCode}. Error: {Error}",
                    model.Type,
                    process.ExitCode,
                    error);
                detections.AddRange(GetSeedDetections(model.Type));
                continue;
            }

            var modelDetections = JsonSerializer.Deserialize<List<DetectionResult>>(
                output,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? [];

            foreach (var detection in modelDetections)
            {
                if (string.IsNullOrWhiteSpace(detection.ModelType))
                {
                    detection.ModelType = model.Type;
                }
            }

            detections.AddRange(modelDetections);
        }

        return detections;
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_environment.ContentRootPath, relativeOrAbsolutePath);
    }

    private static IReadOnlyCollection<DetectionResult> GetSeedDetections(string modelType)
    {
        return modelType switch
        {
            "YoloSmoking" => new[]
            {
                new DetectionResult
                {
                    ModelType = "YoloSmoking",
                    Label = "smoke",
                    Confidence = 0.94m,
                    BoundingBox = "x:132,y:46,w:88,h:90",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            },
            "YoloLeaving" => new[]
            {
                new DetectionResult
                {
                    ModelType = "YoloLeaving",
                    Label = "empty-chair",
                    Confidence = 0.91m,
                    BoundingBox = "x:218,y:74,w:112,h:180",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            },
            _ => new[]
            {
                new DetectionResult
                {
                    ModelType = modelType,
                    Label = "unknown",
                    Confidence = 0.50m,
                    BoundingBox = "x:120,y:40,w:84,h:92",
                    ProcessedAtUtc = DateTime.UtcNow
                }
            }
        };
    }
}
