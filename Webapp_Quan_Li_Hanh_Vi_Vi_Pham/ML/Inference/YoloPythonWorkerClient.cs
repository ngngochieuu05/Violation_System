using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public sealed class YoloPythonWorkerClient : IAsyncDisposable
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<YoloPythonWorkerClient> _logger;
    private readonly YoloModelOptions _options;
    private readonly ConcurrentDictionary<string, WorkerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public YoloPythonWorkerClient(
        IWebHostEnvironment environment,
        IOptions<YoloModelOptions> options,
        ILogger<YoloPythonWorkerClient> logger)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<YoloWorkerResponse> RunAsync(
        YoloWorkerRequest request,
        CancellationToken cancellationToken = default)
    {
        var workerScriptPath = ResolvePath(_options.WorkerScriptPath);
        if (!File.Exists(workerScriptPath))
        {
            throw new FileNotFoundException("Không tìm thấy script worker YOLO.", workerScriptPath);
        }

        var pythonExe = ResolvePythonExecutable();
        var sessionKey = $"{request.ModelPath}|{request.ModelType}|{request.DeviceMode}".ToLowerInvariant();
        var session = _sessions.GetOrAdd(
            sessionKey,
            _ => new WorkerSession(
                pythonExe,
                workerScriptPath,
                request.ModelPath,
                request.ModelType,
                request.DeviceMode,
                request.UseHalfPrecision,
                Math.Max(5, _options.WorkerStartupTimeoutSeconds),
                _jsonOptions,
                _logger));

        return await session.RunAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
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

    private sealed class WorkerSession : IAsyncDisposable
    {
        private readonly string _pythonExe;
        private readonly string _scriptPath;
        private readonly string _modelPath;
        private readonly string _modelType;
        private readonly string _deviceMode;
        private readonly bool _useHalfPrecision;
        private readonly int _startupTimeoutSeconds;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;

        public WorkerSession(
            string pythonExe,
            string scriptPath,
            string modelPath,
            string modelType,
            string deviceMode,
            bool useHalfPrecision,
            int startupTimeoutSeconds,
            JsonSerializerOptions jsonOptions,
            ILogger logger)
        {
            _pythonExe = pythonExe;
            _scriptPath = scriptPath;
            _modelPath = modelPath;
            _modelType = modelType;
            _deviceMode = deviceMode;
            _useHalfPrecision = useHalfPrecision;
            _startupTimeoutSeconds = startupTimeoutSeconds;
            _jsonOptions = jsonOptions;
            _logger = logger;
        }

        public async Task<YoloWorkerResponse> RunAsync(YoloWorkerRequest request, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                await EnsureStartedAsync(cancellationToken);

                var serializedRequest = JsonSerializer.Serialize(
                    new
                    {
                        sourcePath = request.SourcePath,
                        conf = request.ConfThreshold.ToString(CultureInfo.InvariantCulture),
                        iou = request.IouThreshold.ToString(CultureInfo.InvariantCulture),
                        modelType = request.ModelType,
                        label = request.Label,
                        maxFrames = request.MaxFrames,
                        imageSize = request.ImageSize
                    },
                    _jsonOptions);

                await _stdin!.WriteLineAsync(serializedRequest);
                await _stdin.FlushAsync();

                using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                responseTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                var responseLine = await _stdout!.ReadLineAsync(responseTimeout.Token);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    throw new InvalidOperationException("Worker YOLO không trả dữ liệu.");
                }

                var response = JsonSerializer.Deserialize<YoloWorkerResponse>(
                    responseLine,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return response ?? throw new InvalidOperationException("Không parse được kết quả từ worker YOLO.");
            }
            catch
            {
                await RestartAsync();
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lock.Dispose();
            await StopProcessAsync();
        }

        private async Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (_process is { HasExited: false } && _stdin is not null && _stdout is not null)
            {
                return;
            }

            await StopProcessAsync();

            var args =
                $"\"{_scriptPath}\" --model \"{_modelPath}\" --model-type \"{_modelType}\" --device \"{_deviceMode}\" --half {(_useHalfPrecision ? "1" : "0")}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogDebug("[YoloWorker Stderr] {Message}", e.Data);
                }
            };

            process.Start();
            process.BeginErrorReadLine();
            _process = process;
            _stdin = process.StandardInput;
            _stdout = process.StandardOutput;

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(_startupTimeoutSeconds));
            var readyLine = await _stdout.ReadLineAsync(startupTimeout.Token);
            if (string.IsNullOrWhiteSpace(readyLine))
            {
                throw new InvalidOperationException("Worker YOLO không khởi động được.");
            }

            if (!readyLine.Contains("\"status\"", StringComparison.OrdinalIgnoreCase) || 
                !readyLine.Contains("\"ready\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Worker YOLO trả startup không hợp lệ: {readyLine}.");
            }
        }

        private async Task RestartAsync()
        {
            _logger.LogWarning("Restarting YOLO worker session for {ModelType} - {ModelPath}", _modelType, _modelPath);
            await StopProcessAsync();
        }

        private async Task StopProcessAsync()
        {
            try
            {
                if (_stdin is not null)
                {
                    await _stdin.WriteLineAsync("{\"command\":\"shutdown\"}");
                    await _stdin.FlushAsync();
                }
            }
            catch
            {
                // Ignore and terminate the process below.
            }

            _stdin?.Dispose();
            _stdout?.Dispose();
            _stdin = null;
            _stdout = null;

            if (_process is { HasExited: false })
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill errors during cleanup.
                }
            }

            _process?.Dispose();
            _process = null;
        }
    }
}
