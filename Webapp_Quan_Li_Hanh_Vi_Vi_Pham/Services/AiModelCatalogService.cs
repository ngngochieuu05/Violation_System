using System.Text.Json;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Areas.Admin.Models;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public interface IAiModelCatalogService
{
    Task<AdminAiModelsPageViewModel> BuildPageViewModelAsync(
        IReadOnlyCollection<AiModel> models,
        CancellationToken cancellationToken = default);
}

public class AiModelCatalogService : IAiModelCatalogService
{
    public Task<AdminAiModelsPageViewModel> BuildPageViewModelAsync(
        IReadOnlyCollection<AiModel> models,
        CancellationToken cancellationToken = default)
    {
        var items = models
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(BuildCatalogItem)
            .ToList();

        return Task.FromResult(new AdminAiModelsPageViewModel
        {
            Models = items
        });
    }

    private static AdminAiModelCatalogItemViewModel BuildCatalogItem(AiModel model)
    {
        var isYolo = IsYoloModel(model.Type, model.ModelPath);
        var item = new AdminAiModelCatalogItemViewModel
        {
            Id = model.Id,
            Name = model.Name,
            Type = model.Type,
            ModelPath = model.ModelPath,
            ConfThreshold = isYolo
                ? NormalizeThreshold(model.ConfThreshold, 0.25m)
                : NormalizeThreshold(model.ConfThreshold, 0.40m),
            IouThreshold = isYolo ? NormalizeThreshold(model.IouThreshold, 0.45m) : 0m,
            IsActive = model.IsActive,
            CreatedAtUtc = model.CreatedAtUtc,
            ModelFormat = Path.GetExtension(model.ModelPath).ToLowerInvariant(),
            EngineLabel = InferEngine(model.ModelPath)
        };

        var resolvedPath = TryResolveAbsolutePath(model.ModelPath);
        if (resolvedPath is not null && File.Exists(resolvedPath))
        {
            try
            {
                item.FileSizeMb = Math.Round(new FileInfo(resolvedPath).Length / 1024d / 1024d, 2);
            }
            catch
            {
                item.FileSizeMb = null;
            }

            var evalPayload = FindLatestEvalPayload(resolvedPath);
            if (evalPayload is not null)
            {
                item.Accuracy = ExtractFloat(evalPayload, "accuracy", "Top-1 Accuracy", "Top-1 Acc");
                item.MacroF1 = ExtractFloat(evalPayload, "macro_f1", "Macro F1-Score", "F1 Score", "Box F1", "Mask F1");
                item.Map50 = ExtractFloat(evalPayload, "val_map50", "mAP@50", "Box mAP@50", "Mask mAP@50", "mAP50");
                item.Map50To95 = ExtractFloat(evalPayload, "val_map50_95", "mAP@50-95", "Box mAP@50-95", "Mask mAP@50-95", "mAP50-95");
                item.EvalSplit = GetString(evalPayload, "split");
                item.ArtifactSource = GetString(evalPayload, "_eval_json_path");
                item.ClassNames = ExtractClassNames(evalPayload);
            }
        }

        return item;
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

    private static bool IsYoloModel(string? type, string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(type)
            && type.StartsWith("Yolo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(modelPath ?? string.Empty);
        return extension.Equals(".pt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase);
    }

    private static string InferEngine(string modelPath)
    {
        return Path.GetExtension(modelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
            ? "Ultralytics + ONNX Runtime"
            : Path.GetExtension(modelPath).Equals(".pt", StringComparison.OrdinalIgnoreCase)
                ? "Ultralytics YOLO"
                : "DeepFace / Tuỳ chọn";
    }

    private static string? TryResolveAbsolutePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var cleaned = rawPath.Trim().Replace("/", "\\", StringComparison.Ordinal);
        if (cleaned.StartsWith("D:WEB", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Insert(2, "\\");
        }

        return Path.IsPathRooted(cleaned) ? cleaned : null;
    }

    private static Dictionary<string, JsonElement>? FindLatestEvalPayload(string modelPath)
    {
        var modelFile = new FileInfo(modelPath);
        var searchRoots = new List<DirectoryInfo?>
        {
            modelFile.Directory,
            modelFile.Directory?.Parent,
            modelFile.Directory?.Parent?.Parent,
            new DirectoryInfo(@"D:\DACS\Con_Bo_Cuoi_App\tool_train")
        };

        Dictionary<string, JsonElement>? bestPayload = null;
        DateTime bestTime = DateTime.MinValue;
        var targetName = modelFile.Name;
        var targetPath = NormalizePath(modelFile.FullName);

        foreach (var root in searchRoots.Where(static item => item is not null && item.Exists).DistinctBy(item => item!.FullName))
        {
            foreach (var evalFile in root!.EnumerateFiles("eval*.json", SearchOption.AllDirectories))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(evalFile.FullName));
                    var payload = document.RootElement;
                    if (payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var modelField = GetString(payload, "model_path", "model");
                    var matched = false;
                    if (!string.IsNullOrWhiteSpace(modelField))
                    {
                        var modelFieldName = Path.GetFileName(modelField);
                        var modelFieldPath = NormalizePath(modelField);
                        matched = string.Equals(modelFieldPath, targetPath, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(modelFieldName, targetName, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        matched = evalFile.Name.Contains(targetName, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matched)
                    {
                        continue;
                    }

                    var split = GetString(payload, "split");
                    if (!string.IsNullOrWhiteSpace(split) && !string.Equals(split, "test", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (evalFile.LastWriteTimeUtc <= bestTime)
                    {
                        continue;
                    }

                    bestTime = evalFile.LastWriteTimeUtc;
                    bestPayload = payload.EnumerateObject()
                        .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.OrdinalIgnoreCase);
                    bestPayload["_eval_json_path"] = JsonDocument.Parse($"\"{evalFile.FullName.Replace("\\", "\\\\", StringComparison.Ordinal)}\"").RootElement.Clone();
                }
                catch
                {
                    // Ignore invalid eval artifact files.
                }
            }
        }

        return bestPayload;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal).Trim().ToLowerInvariant();
    }

    private static string GetString(IReadOnlyDictionary<string, JsonElement> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string GetString(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static double? ExtractFloat(IReadOnlyDictionary<string, JsonElement> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetValue(key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numericValue))
            {
                return numericValue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString();
                if (double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out numericValue))
                {
                    return numericValue;
                }

                var digits = new string((raw ?? string.Empty).Where(static ch => char.IsDigit(ch) || ch is '.' or ',').ToArray());
                if (double.TryParse(
                    digits.Replace(",", ".", StringComparison.Ordinal),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out numericValue))
                {
                    return numericValue;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractClassNames(IReadOnlyDictionary<string, JsonElement> payload)
    {
        if (!payload.TryGetValue("class_names", out var classNamesElement) || classNamesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return classNamesElement
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString() ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}
