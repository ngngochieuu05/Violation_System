using Microsoft.Extensions.Options;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramCommandPollingHostedService : BackgroundService
{
    private readonly ITelegramAlertService _telegramAlertService;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramCommandPollingHostedService> _logger;

    public TelegramCommandPollingHostedService(
        ITelegramAlertService telegramAlertService,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramCommandPollingHostedService> logger)
    {
        _telegramAlertService = telegramAlertService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken) || _options.CommandPollingIntervalSeconds <= 0)
        {
            _logger.LogInformation("Telegram command polling is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CommandPollingIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _telegramAlertService.ProcessPendingUpdatesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram command polling failed.");
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
}
