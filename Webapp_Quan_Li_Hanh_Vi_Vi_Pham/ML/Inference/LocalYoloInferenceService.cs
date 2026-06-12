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
        var activeModel = await _context.AiModels.FirstOrDefaultAsync(m => m.Type.StartsWith("Yolo") && m.IsActive, cancellationToken);
        
        var scriptPath = ResolvePath(_options.InferenceScriptPath);
        var modelPath = activeModel != null ? activeModel.ModelPath : ResolvePath(_options.ModelPath);
        var sampleSourcePath = ResolvePath(_options.SampleSourcePath);
        var conf = activeModel != null ? activeModel.ConfThreshold : 0.25m;
        var iou = activeModel != null ? activeModel.IouThreshold : 0.45m;

        // If the path isn't rooted, resolve it relative to ContentRootPath
        if (!Path.IsPathRooted(modelPath))
        {
            modelPath = ResolvePath(modelPath);
        }

        if (!File.Exists(scriptPath) || !File.Exists(modelPath) || !File.Exists(sampleSourcePath))
        {
            _logger.LogInformation(
                "YOLO local files are not ready (script: {ScriptExists}, model: {ModelExists}, sample: {SampleExists}). Returning seeded detections.",
                File.Exists(scriptPath), File.Exists(modelPath), File.Exists(sampleSourcePath));
            return GetSeedDetections();
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            Arguments =
                $"\"{scriptPath}\" --model \"{modelPath}\" --source \"{sampleSourcePath}\" --conf {conf} --iou {iou}",
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
                "YOLO local inference failed with exit code {ExitCode}. Error: {Error}",
                process.ExitCode,
                error);
            return GetSeedDetections();
        }

        var detections = JsonSerializer.Deserialize<List<DetectionResult>>(
            output,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return detections ?? [];
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(_environment.ContentRootPath, relativeOrAbsolutePath);
    }

    private static IReadOnlyCollection<DetectionResult> GetSeedDetections()
    {
        return
        [
            new DetectionResult
            {
                Label = "no-helmet",
                Confidence = 0.94m,
                BoundingBox = "x:132,y:46,w:88,h:90",
                ProcessedAtUtc = DateTime.UtcNow.AddMinutes(-10)
            },
            new DetectionResult
            {
                Label = "restricted-area-entry",
                Confidence = 0.89m,
                BoundingBox = "x:218,y:74,w:112,h:180",
                ProcessedAtUtc = DateTime.UtcNow.AddMinutes(-4)
            }
        ];
    }
}
