using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class LocalYoloInferenceService : IYoloInferenceService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalYoloInferenceService> _logger;
    private readonly YoloModelOptions _options;

    public LocalYoloInferenceService(
        IWebHostEnvironment environment,
        IOptions<YoloModelOptions> options,
        ILogger<LocalYoloInferenceService> logger)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyCollection<DetectionResult>> GetLatestDetectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var scriptPath = ResolvePath(_options.InferenceScriptPath);
        var modelPath = ResolvePath(_options.ModelPath);
        var sampleSourcePath = ResolvePath(_options.SampleSourcePath);

        if (!File.Exists(scriptPath) || !File.Exists(modelPath) || !File.Exists(sampleSourcePath))
        {
            _logger.LogInformation(
                "YOLO local files are not ready. Returning seeded detections for dashboard bootstrap.");
            return GetSeedDetections();
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.PythonExecutable,
            Arguments =
                $"\"{scriptPath}\" --model \"{modelPath}\" --source \"{sampleSourcePath}\"",
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
