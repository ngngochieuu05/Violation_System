using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

public class ViolationMonitoringHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ViolationMonitoringOptions _options;
    private readonly ILogger<ViolationMonitoringHostedService> _logger;
    private readonly IViolationMonitoringOrchestrator _orchestrator;

    public ViolationMonitoringHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<ViolationMonitoringOptions> options,
        ILogger<ViolationMonitoringHostedService> logger,
        IViolationMonitoringOrchestrator orchestrator)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _orchestrator = orchestrator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.PollingIntervalSeconds <= 0)
        {
            _logger.LogWarning("Violation monitoring is disabled because PollingIntervalSeconds <= 0.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Violation monitoring loop failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var inferenceService = scope.ServiceProvider.GetRequiredService<IYoloInferenceService>();
        var detections = await inferenceService.GetLatestDetectionsAsync(cancellationToken);
        await _orchestrator.ProcessDetectionsAsync(detections, cancellationToken);
    }
}
