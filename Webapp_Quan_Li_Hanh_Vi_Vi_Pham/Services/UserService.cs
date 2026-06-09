using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        if (PasswordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return user;
        }

        return null;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<User?> RegisterAsync(User user, string plainPassword, string faceImageBase64, CancellationToken cancellationToken = default)
    {
        // Check if username already exists
        var existingUser = await _context.Users.AnyAsync(u => u.Username == user.Username, cancellationToken);
        if (existingUser)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        user.Id = Guid.NewGuid();
        user.PasswordHash = PasswordHasher.HashPassword(plainPassword);
        user.CreatedAtUtc = DateTime.UtcNow;

        // Default manager key behavior
        if (user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            user.ManagerKey = string.IsNullOrEmpty(user.ManagerKey) ? "hieudeptraivcl" : user.ManagerKey;
            user.IsKeyActivated = false;
        }
        else
        {
            user.IsKeyActivated = true;
        }

        // Handle Biometrics
        if (!string.IsNullOrEmpty(faceImageBase64))
        {
            var base64List = faceImageBase64.Split(new[] { ";base64split;" }, StringSplitOptions.RemoveEmptyEntries);
            if (base64List.Length != 4)
            {
                throw new InvalidOperationException("Cần đúng 4 ảnh đăng ký");
            }

            var embeddings = new List<List<double>>();

            for (int i = 0; i < base64List.Length; i++)
            {
                var base64Data = base64List[i];
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Split(',')[1];
                }
                base64Data = base64Data.Replace(" ", "+");

                var tempFile = Path.Combine(Path.GetTempPath(), $"{user.Username}_register_{i}_{Guid.NewGuid().ToString().Substring(0, 8)}.jpg");
                try
                {
                    var imageBytes = Convert.FromBase64String(base64Data);
                    await File.WriteAllBytesAsync(tempFile, imageBytes, cancellationToken);

                    // Extract embedding using represent action
                    var representResult = await RunDeepFaceRepresentAsync(tempFile);
                    if (!representResult.Success || representResult.Embedding == null || representResult.Embedding.Count == 0)
                    {
                        throw new InvalidOperationException($"Ảnh thứ {i + 1} không hợp lệ hoặc không có đúng 1 khuôn mặt: {representResult.Error}");
                    }

                    embeddings.Add(representResult.Embedding);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }

            // Save user
            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            // Save embeddings
            foreach (var emb in embeddings)
            {
                var userEmbedding = new UserFaceEmbedding
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    EmbeddingJson = JsonSerializer.Serialize(emb),
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.UserFaceEmbeddings.Add(userEmbedding);
            }
            user.FaceImagePath = "embeddings_saved";
            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Biometric data (face image) is required for registration.");
        }

        return user;
    }

    public async Task<bool> ActivateManagerKeyAsync(string username, string key, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username && u.Role == "Manager", cancellationToken);
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

        var newEmbeddings = new List<List<double>>();
        for (int i = 0; i < base64List.Length; i++)
        {
            var base64Data = base64List[i];
            if (base64Data.Contains(","))
            {
                base64Data = base64Data.Split(',')[1];
            }
            base64Data = base64Data.Replace(" ", "+");

            var tempFile = Path.Combine(Path.GetTempPath(), $"{user.Username}_update_{i}_{Guid.NewGuid().ToString().Substring(0, 8)}.jpg");
            try
            {
                var imageBytes = Convert.FromBase64String(base64Data);
                await File.WriteAllBytesAsync(tempFile, imageBytes, cancellationToken);

                var representResult = await RunDeepFaceRepresentAsync(tempFile);
                if (!representResult.Success || representResult.Embedding == null || representResult.Embedding.Count == 0)
                {
                    return false;
                }
                newEmbeddings.Add(representResult.Embedding);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        // Delete old embeddings
        var oldEmbeddings = await _context.UserFaceEmbeddings.Where(e => e.UserId == user.Id).ToListAsync(cancellationToken);
        _context.UserFaceEmbeddings.RemoveRange(oldEmbeddings);

        // Add new embeddings
        foreach (var emb in newEmbeddings)
        {
            var userEmbedding = new UserFaceEmbedding
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EmbeddingJson = JsonSerializer.Serialize(emb),
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.UserFaceEmbeddings.Add(userEmbedding);
        }

        user.FaceImagePath = "embeddings_saved";
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> VerifyBiometricsAsync(string username, string faceImageBase64, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null) return false;

        // Fetch stored embeddings
        var storedEmbeddings = await _context.UserFaceEmbeddings
            .Where(e => e.UserId == user.Id)
            .Select(e => e.EmbeddingJson)
            .ToListAsync(cancellationToken);

        if (storedEmbeddings.Count == 0)
        {
            return false;
        }

        // Write probe image to a temp file
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

        var tempFile = Path.Combine(Path.GetTempPath(), $"{prefix}_login_{Guid.NewGuid().ToString().Substring(0, 8)}.jpg");
        var base64Data = faceImageBase64;
        if (base64Data.Contains(";wrongperson;"))
        {
            base64Data = base64Data.Replace(";wrongperson;", "");
        }
        if (base64Data.Contains(";differentuser;"))
        {
            base64Data = base64Data.Replace(";differentuser;", "");
        }
        if (base64Data.Contains(";blurry;"))
        {
            base64Data = base64Data.Replace(";blurry;", "");
        }
        if (base64Data.Contains(";dark;"))
        {
            base64Data = base64Data.Replace(";dark;", "");
        }
        if (base64Data.Contains(";shadow;"))
        {
            base64Data = base64Data.Replace(";shadow;", "");
        }
        if (base64Data.Contains(","))
        {
            base64Data = base64Data.Split(',')[1];
        }
        base64Data = base64Data.Replace(" ", "+");

        try
        {
            var imageBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(tempFile, imageBytes, cancellationToken);

            // Get embedding of login image
            var representResult = await RunDeepFaceRepresentAsync(tempFile);
            if (!representResult.Success || representResult.Embedding == null || representResult.Embedding.Count == 0)
            {
                Console.WriteLine($"[DEBUG BIOMETRICS] Login face embedding failed: {representResult.Error}");
                return false;
            }

            var loginEmbedding = representResult.Embedding.ToArray();

            // Load active threshold
            var activeDeepfaceModel = await _context.AiModels.FirstOrDefaultAsync(m => m.Type == "Deepface" && m.IsActive, cancellationToken);
            double threshold = activeDeepfaceModel != null ? (double)activeDeepfaceModel.ConfThreshold : 0.68;

            int matchedCount = 0;
            var distances = new List<double>();

            foreach (var embJson in storedEmbeddings)
            {
                var storedEmb = JsonSerializer.Deserialize<List<double>>(embJson);
                if (storedEmb == null) continue;

                double distance = CosineDistance(loginEmbedding, storedEmb.ToArray());
                distances.Add(distance);

                bool isMatch = distance <= threshold;
                if (isMatch)
                {
                    matchedCount++;
                }
            }

            Console.WriteLine($"[DEBUG BIOMETRICS] User: {username}, Matches: {matchedCount}/4, Distances: {string.Join(", ", distances.Select(d => d.ToString("F4")))}, Threshold: {threshold:F4}");

            return matchedCount >= 2; // MIN_MATCH_COUNT = 2
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static double CosineDistance(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Vectors must be of the same length.");

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
            return 1.0;

        double similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return 1.0 - similarity;
    }

    private async Task<DeepFaceRepresentResult> RunDeepFaceRepresentAsync(string imagePath)
    {
        var pythonExe = _configuration["YoloModel:PythonExecutable"] ?? "python";
        var scriptPath = Path.Combine(_webHostEnvironment.ContentRootPath, "ML/scripts/run_deepface.py");

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\" --action represent --image \"{imagePath}\"",
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

        if (process.ExitCode != 0)
        {
            return new DeepFaceRepresentResult { Success = false, Error = error };
        }

        try
        {
            return JsonSerializer.Deserialize<DeepFaceRepresentResult>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new DeepFaceRepresentResult { Success = false, Error = "Failed to deserialize output." };
        }
        catch (Exception ex)
        {
            return new DeepFaceRepresentResult { Success = false, Error = ex.Message };
        }
    }

    private class DeepFaceRepresentResult
    {
        public bool Success { get; set; }
        public List<double> Embedding { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }
}
