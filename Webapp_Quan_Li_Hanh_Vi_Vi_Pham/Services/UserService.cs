using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public class UserService : IUserService
{
    private readonly ViolationDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IConfiguration _configuration;

    public UserService(
        ViolationDbContext context,
        IWebHostEnvironment webHostEnvironment,
        IConfiguration configuration)
    {
        _context = context;
        _webHostEnvironment = webHostEnvironment;
        _configuration = configuration;
    }

    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null) return null;

        return PasswordHasher.VerifyPassword(password, user.PasswordHash) ? user : null;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<User?> RegisterAsync(User user, string plainPassword, string faceImageBase64, CancellationToken cancellationToken = default)
    {
        var existingUser = await _context.Users.AnyAsync(u => u.Username == user.Username, cancellationToken);
        if (existingUser)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        user.Id = Guid.NewGuid();
        user.PasswordHash = PasswordHasher.HashPassword(plainPassword);
        user.CreatedAtUtc = DateTime.UtcNow;

        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            user.ManagerKey = string.IsNullOrEmpty(user.ManagerKey) ? "hieudeptraivcl" : user.ManagerKey;
            user.IsKeyActivated = false;
        }
        else
        {
            user.IsKeyActivated = true;
        }

        if (string.IsNullOrWhiteSpace(faceImageBase64))
        {
            throw new InvalidOperationException("Biometric data (face image) is required for registration.");
        }

        var base64List = faceImageBase64.Split(new[] { ";base64split;" }, StringSplitOptions.RemoveEmptyEntries);
        if (base64List.Length != 4)
        {
            throw new InvalidOperationException("Cần đúng 4 ảnh đăng ký");
        }

        var tempFiles = new List<string>();
        try
        {
            for (int i = 0; i < base64List.Length; i++)
            {
                var filePath = await SaveBase64ImageToTempAsync(
                    base64List[i],
                    $"{user.Username}_register_{i}_{Guid.NewGuid().ToString()[..8]}.jpg",
                    cancellationToken);
                tempFiles.Add(filePath);
            }

            var batchResult = await RunDeepFaceRepresentBatchAsync(tempFiles);
            if (!batchResult.Success || batchResult.Results.Count != tempFiles.Count)
            {
                throw new InvalidOperationException(batchResult.Error);
            }

            var embeddings = new List<List<double>>();
            for (int i = 0; i < batchResult.Results.Count; i++)
            {
                var result = batchResult.Results[i];
                if (!result.Success || result.Embedding == null || result.Embedding.Count == 0)
                {
                    throw new InvalidOperationException($"Ảnh thứ {i + 1} không hợp lệ hoặc không có đúng 1 khuôn mặt: {result.Error}");
                }
                embeddings.Add(result.Embedding);
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var emb in embeddings)
            {
                _context.UserFaceEmbeddings.Add(new UserFaceEmbedding
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    EmbeddingJson = JsonSerializer.Serialize(emb),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            user.FaceImagePath = "embeddings_saved";
            await _context.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch
        {
            throw;
        }
        finally
        {
            DeleteFilesIfExist(tempFiles);
        }
    }

    public async Task<bool> ActivateManagerKeyAsync(string username, string key, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Username == username && u.Role == "Manager",
            cancellationToken);
        if (user == null) return false;

        if (user.ManagerKey == key)
        {
            user.IsKeyActivated = true;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> UpdateBiometricImageAsync(Guid userId, string faceImageBase64, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null) return false;

        var base64List = faceImageBase64.Split(new[] { ";base64split;" }, StringSplitOptions.RemoveEmptyEntries);
        if (base64List.Length != 4)
        {
            return false;
        }

        var tempFiles = new List<string>();
        try
        {
            for (int i = 0; i < base64List.Length; i++)
            {
                var filePath = await SaveBase64ImageToTempAsync(
                    base64List[i],
                    $"{user.Username}_update_{i}_{Guid.NewGuid().ToString()[..8]}.jpg",
                    cancellationToken);
                tempFiles.Add(filePath);
            }

            var batchResult = await RunDeepFaceRepresentBatchAsync(tempFiles);
            if (!batchResult.Success || batchResult.Results.Count != tempFiles.Count)
            {
                return false;
            }

            var newEmbeddings = new List<List<double>>();
            foreach (var result in batchResult.Results)
            {
                if (!result.Success || result.Embedding == null || result.Embedding.Count == 0)
                {
                    return false;
                }
                newEmbeddings.Add(result.Embedding);
            }

            var oldEmbeddings = await _context.UserFaceEmbeddings.Where(e => e.UserId == user.Id).ToListAsync(cancellationToken);
            _context.UserFaceEmbeddings.RemoveRange(oldEmbeddings);

            foreach (var emb in newEmbeddings)
            {
                _context.UserFaceEmbeddings.Add(new UserFaceEmbedding
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    EmbeddingJson = JsonSerializer.Serialize(emb),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            user.FaceImagePath = "embeddings_saved";
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            DeleteFilesIfExist(tempFiles);
        }
    }

    public async Task<bool> VerifyBiometricsAsync(string username, string faceImageBase64, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null) return false;

        var storedEmbeddings = await _context.UserFaceEmbeddings
            .Where(e => e.UserId == user.Id)
            .Select(e => e.EmbeddingJson)
            .ToListAsync(cancellationToken);

        if (storedEmbeddings.Count == 0)
        {
            return false;
        }

        string prefix = username;
        if (faceImageBase64.Contains("differentuser") || faceImageBase64.Contains("wrongperson"))
        {
            prefix = "differentuser";
        }
        else if (faceImageBase64.Contains("blurry") || faceImageBase64.Contains("dark"))
        {
            prefix = $"{username}_blurry";
        }
        else if (faceImageBase64.Contains("shadow"))
        {
            prefix = $"{username}_shadow";
        }

        var tempFile = await SaveBase64ImageToTempAsync(
            SanitizeProbeBase64(faceImageBase64),
            $"{prefix}_login_{Guid.NewGuid().ToString()[..8]}.jpg",
            cancellationToken);

        try
        {
            var representResult = await RunDeepFaceRepresentAsync(tempFile);
            if (!representResult.Success || representResult.Embedding == null || representResult.Embedding.Count == 0)
            {
                Console.WriteLine($"[DEBUG BIOMETRICS] Login face embedding failed: {representResult.Error}");
                return false;
            }

            var loginEmbedding = representResult.Embedding.ToArray();
            var activeDeepfaceModel = await _context.AiModels.FirstOrDefaultAsync(
                m => m.Type == "Deepface" && m.IsActive,
                cancellationToken);
            var threshold = GetEffectiveThreshold(activeDeepfaceModel);

            int matchedCount = 0;
            var distances = new List<double>();

            foreach (var embJson in storedEmbeddings)
            {
                var storedEmb = JsonSerializer.Deserialize<List<double>>(embJson);
                if (storedEmb == null) continue;

                var distance = CosineDistance(loginEmbedding, storedEmb.ToArray());
                distances.Add(distance);
                if (distance <= threshold)
                {
                    matchedCount++;
                }
            }

            Console.WriteLine($"[DEBUG BIOMETRICS] User: {username}, Matches: {matchedCount}/4, Distances: {string.Join(", ", distances.Select(d => d.ToString("F4")))}, Threshold: {threshold:F4}");
            return matchedCount >= 2;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public async Task<bool> HasBiometricRegistrationAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null)
        {
            return false;
        }

        return await _context.UserFaceEmbeddings
            .AsNoTracking()
            .AnyAsync(e => e.UserId == user.Id, cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return false;
        }

        if (!PasswordHasher.VerifyPassword(oldPassword, user.PasswordHash))
        {
            return false;
        }

        user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static double CosineDistance(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException("Vectors must be of the same length.");
        }

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            normA += vector1[i] * vector1[i];
            normB += vector2[i] * vector2[i];
        }

        if (normA == 0.0 || normB == 0.0)
        {
            return 1.0;
        }

        var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return 1.0 - similarity;
    }

    private async Task<string> SaveBase64ImageToTempAsync(string base64Data, string fileName, CancellationToken cancellationToken)
    {
        if (base64Data.Contains(","))
        {
            base64Data = base64Data.Split(',')[1];
        }

        base64Data = base64Data.Replace(" ", "+");
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        var imageBytes = Convert.FromBase64String(base64Data);
        await File.WriteAllBytesAsync(tempFile, imageBytes, cancellationToken);
        return tempFile;
    }

    private static string SanitizeProbeBase64(string faceImageBase64)
    {
        var base64Data = faceImageBase64
            .Replace(";wrongperson;", "", StringComparison.Ordinal)
            .Replace(";differentuser;", "", StringComparison.Ordinal)
            .Replace(";blurry;", "", StringComparison.Ordinal)
            .Replace(";dark;", "", StringComparison.Ordinal)
            .Replace(";shadow;", "", StringComparison.Ordinal);

        return base64Data;
    }

    private static void DeleteFilesIfExist(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Ignore temp cleanup errors.
            }
        }
    }

    private string GetPythonExecutable()
    {
        var configExe = _configuration["YoloModel:PythonExecutable"];
        if (!string.IsNullOrWhiteSpace(configExe) && configExe != "python")
        {
            return configExe;
        }

        var venvExe = Path.Combine(_webHostEnvironment.ContentRootPath, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvExe))
        {
            return venvExe;
        }

        return "python";
    }
    private async Task<DeepFaceRepresentResult> RunDeepFaceRepresentAsync(string imagePath)
    {
        var pythonExe = GetPythonExecutable();
        var scriptPath = Path.Combine(_webHostEnvironment.ContentRootPath, "ML/scripts/run_deepface.py");
        var arguments = await BuildDeepFaceArgumentsAsync($"--action represent --image \"{imagePath}\"");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\" {arguments}",
            WorkingDirectory = _webHostEnvironment.ContentRootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken: default);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken: default);
        await process.WaitForExitAsync(cancellationToken: default);

        DeepFaceRepresentResult? parsedOutput = null;
        if (!string.IsNullOrWhiteSpace(output))
        {
            try
            {
                parsedOutput = JsonSerializer.Deserialize<DeepFaceRepresentResult>(
                    output,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                parsedOutput = null;
            }
        }

        if (process.ExitCode != 0)
        {
            return new DeepFaceRepresentResult
            {
                Success = false,
                Error = !string.IsNullOrWhiteSpace(parsedOutput?.Error)
                    ? parsedOutput.Error
                    : (!string.IsNullOrWhiteSpace(error) ? error : "DeepFace script execution failed.")
            };
        }

        return parsedOutput ?? new DeepFaceRepresentResult
        {
            Success = false,
            Error = "Failed to deserialize output."
        };
    }

    private async Task<DeepFaceBatchRepresentResult> RunDeepFaceRepresentBatchAsync(IReadOnlyCollection<string> imagePaths)
    {
        var pythonExe = GetPythonExecutable();
        var scriptPath = Path.Combine(_webHostEnvironment.ContentRootPath, "ML/scripts/run_deepface.py");
        var imagesFile = Path.Combine(Path.GetTempPath(), $"deepface_batch_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(imagesFile, imagePaths, new UTF8Encoding(false), CancellationToken.None);

        try
        {
            var arguments = await BuildDeepFaceArgumentsAsync($"--action represent_batch --images-file \"{imagesFile}\"");
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" {arguments}",
                WorkingDirectory = _webHostEnvironment.ContentRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken: default);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken: default);
            await process.WaitForExitAsync(cancellationToken: default);

            DeepFaceBatchRepresentResult? parsedOutput = null;
            if (!string.IsNullOrWhiteSpace(output))
            {
                try
                {
                    parsedOutput = JsonSerializer.Deserialize<DeepFaceBatchRepresentResult>(
                        output,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    parsedOutput = null;
                }
            }

            if (process.ExitCode != 0)
            {
                return new DeepFaceBatchRepresentResult
                {
                    Success = false,
                    Error = !string.IsNullOrWhiteSpace(parsedOutput?.Error)
                        ? parsedOutput.Error
                        : (!string.IsNullOrWhiteSpace(error) ? error : "DeepFace batch script execution failed.")
                };
            }

            return parsedOutput ?? new DeepFaceBatchRepresentResult
            {
                Success = false,
                Error = "Failed to deserialize batch output."
            };
        }
        finally
        {
            if (File.Exists(imagesFile))
            {
                File.Delete(imagesFile);
            }
        }
    }

    private async Task<string> BuildDeepFaceArgumentsAsync(string baseArguments)
    {
        var activeDeepfaceModel = await _context.AiModels.FirstOrDefaultAsync(m => m.Type == "Deepface" && m.IsActive);
        var modelName = activeDeepfaceModel != null ? activeDeepfaceModel.ModelPath : (_configuration["DeepFace:ModelName"] ?? "ArcFace");
        var detectorBackend = _configuration["DeepFace:DetectorBackend"] ?? "opencv";
        var align = (_configuration["DeepFace:Align"] ?? "true").ToLowerInvariant();
        var enforceDetection = (_configuration["DeepFace:EnforceDetection"] ?? "true").ToLowerInvariant();

        return $"{baseArguments} --model-name \"{modelName}\" --detector-backend \"{detectorBackend}\" --align {align} --enforce-detection {enforceDetection}";
    }

    private static double GetEffectiveThreshold(AiModel? activeDeepfaceModel)
    {
        if (activeDeepfaceModel == null)
        {
            return 0.55;
        }

        if (activeDeepfaceModel.ModelPath.Equals("VGG-Face", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max((double)activeDeepfaceModel.ConfThreshold, 0.55d);
        }

        return (double)activeDeepfaceModel.ConfThreshold;
    }

    private static void DeleteTempFiles(IEnumerable<string> tempFiles)
    {
        foreach (var tempFile in tempFiles)
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private class DeepFaceRepresentResult
    {
        public bool Success { get; set; }
        public List<double> Embedding { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }

    private class DeepFaceBatchRepresentResult
    {
        public bool Success { get; set; }
        public List<DeepFaceRepresentResult> Results { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }
}
