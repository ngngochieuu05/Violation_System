using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramAlertService : ITelegramAlertService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramBotOptions _options;
    private readonly TelegramBotState _state;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramAlertService> _logger;

    public TelegramAlertService(
        HttpClient httpClient,
        IOptions<TelegramBotOptions> options,
        TelegramBotState state,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramAlertService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _state = state;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task SendAlertAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!CanSendTelegram())
        {
            return;
        }

        foreach (var chatId in GetTargetChatIds())
        {
            try
            {
                var response = await SendMessageCoreAsync(chatId, message, cancellationToken: cancellationToken);
                if (!response.Success)
                {
                    _logger.LogWarning("Telegram alert failed for chat {ChatId}. Result: {Summary}", chatId, response.ResponseSummary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram alert threw for chat {ChatId}", chatId);
            }
        }
    }

    public async Task SendViolationAlertAsync(ViolationRecord violation, string message, CancellationToken cancellationToken = default)
    {
        if (!CanSendTelegram())
        {
            return;
        }

        var keyboard = BuildViolationInlineKeyboard(violation);
        var payloadText = BuildViolationNotificationText(violation, message);

        foreach (var chatId in GetTargetChatIds())
        {
            try
            {
                var response = await SendMessageCoreAsync(
                    chatId,
                    payloadText,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);

                if (!response.Success)
                {
                    _logger.LogWarning("Telegram violation alert failed for chat {ChatId}. Result: {Summary}", chatId, response.ResponseSummary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram violation alert threw for chat {ChatId}", chatId);
            }
        }
    }

    public async Task<IReadOnlyCollection<TelegramChatUpdate>> GetRecentUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return [];
        }

        return await GetUpdatesCoreAsync(null, cancellationToken);
    }

    public async Task<TelegramSendResult> SendTestMessageAsync(
        string message,
        string? chatId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveChatId = string.IsNullOrWhiteSpace(chatId)
            ? GetTargetChatIds().FirstOrDefault() ?? string.Empty
            : chatId;

        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return new TelegramSendResult
            {
                Success = false,
                ChatId = effectiveChatId,
                Message = message,
                ResponseSummary = "Bot token chua duoc cau hinh."
            };
        }

        if (string.IsNullOrWhiteSpace(effectiveChatId))
        {
            return new TelegramSendResult
            {
                Success = false,
                ChatId = string.Empty,
                Message = message,
                ResponseSummary = "Chua co chat id de gui thu."
            };
        }

        return await SendMessageCoreAsync(effectiveChatId, message, cancellationToken: cancellationToken);
    }

    public async Task<int> ProcessPendingUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
        {
            return 0;
        }

        var updates = await GetUpdatesCoreAsync(_state.LastProcessedUpdateId + 1, cancellationToken);
        if (updates.Count == 0)
        {
            return 0;
        }

        var handledCount = 0;
        foreach (var update in updates.OrderBy(x => x.UpdateId))
        {
            _state.LastProcessedUpdateId = Math.Max(_state.LastProcessedUpdateId, update.UpdateId);
            _state.RememberChatId(update.ChatId);

            if (update.IsCallbackQuery)
            {
                handledCount += await HandleCallbackAsync(update, cancellationToken);
                continue;
            }

            handledCount += await HandleTextCommandAsync(update, cancellationToken);
        }

        return handledCount;
    }

    public IReadOnlyCollection<string> GetKnownChatIds()
    {
        return _state.GetKnownChatIds();
    }

    private bool CanSendTelegram()
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogWarning("Telegram bot token is not configured. Skipping alert.");
            return false;
        }

        if (GetTargetChatIds().Count == 0)
        {
            _logger.LogWarning("Telegram chat ids are not configured and no dynamic chat ids were discovered. Skipping alert.");
            return false;
        }

        return true;
    }

    private async Task<int> HandleTextCommandAsync(TelegramChatUpdate update, CancellationToken cancellationToken)
    {
        var normalizedText = (update.MessageText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return 0;
        }

        var segments = normalizedText.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = segments[0].ToLowerInvariant();
        var argument1 = segments.Length > 1 ? segments[1] : string.Empty;
        var argument2 = segments.Length > 2 ? segments[2] : string.Empty;

        switch (command)
        {
            case "/start":
                await SendMessageCoreAsync(update.ChatId, BuildWelcomeMessage(update.ChatId), cancellationToken: cancellationToken);
                return 1;

            case "/help":
                await SendMessageCoreAsync(update.ChatId, BuildHelpMessage(), cancellationToken: cancellationToken);
                return 1;

            case "/chatid":
                await SendMessageCoreAsync(update.ChatId, $"Chat ID cua ban la: {update.ChatId}", cancellationToken: cancellationToken);
                return 1;

            case "/status":
                await SendMessageCoreAsync(update.ChatId, await BuildStatusMessageAsync(cancellationToken), cancellationToken: cancellationToken);
                return 1;

            case "/pending":
                await SendMessageCoreAsync(update.ChatId, await BuildPendingMessageAsync(cancellationToken), cancellationToken: cancellationToken);
                return 1;

            case "/latest":
                await SendMessageCoreAsync(update.ChatId, await BuildLatestMessageAsync(cancellationToken), cancellationToken: cancellationToken);
                return 1;

            case "/violation":
                await SendMessageCoreAsync(update.ChatId, await BuildViolationDetailMessageAsync(argument1, cancellationToken), cancellationToken: cancellationToken);
                return 1;

            case "/approve":
                await SendMessageCoreAsync(
                    update.ChatId,
                    await ReviewViolationFromTelegramAsync(argument1, "Approved", update, "Lenh /approve", cancellationToken),
                    cancellationToken: cancellationToken);
                return 1;

            case "/reject":
                await SendMessageCoreAsync(
                    update.ChatId,
                    await ReviewViolationFromTelegramAsync(argument1, "Rejected", update, string.IsNullOrWhiteSpace(argument2) ? "Lenh /reject" : argument2, cancellationToken),
                    cancellationToken: cancellationToken);
                return 1;

            default:
                await SendMessageCoreAsync(
                    update.ChatId,
                    "Lenh khong hop le. Gui /help de xem danh sach lenh duoc ho tro.",
                    cancellationToken: cancellationToken);
                return 1;
        }
    }

    private async Task<int> HandleCallbackAsync(TelegramChatUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return 0;
        }

        var parts = update.CallbackData.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "violation", StringComparison.OrdinalIgnoreCase))
        {
            await AnswerCallbackQueryAsync(update.CallbackQueryId, "Callback khong duoc ho tro.", cancellationToken);
            return 1;
        }

        var action = parts[1];
        var trackingId = parts[2];

        if (string.Equals(action, "detail", StringComparison.OrdinalIgnoreCase))
        {
            await AnswerCallbackQueryAsync(update.CallbackQueryId, $"Dang mo chi tiet {trackingId}", cancellationToken);
            await SendMessageCoreAsync(update.ChatId, await BuildViolationDetailMessageAsync(trackingId, cancellationToken), cancellationToken: cancellationToken);
            return 1;
        }

        if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
        {
            var result = await ReviewViolationFromTelegramAsync(trackingId, "Approved", update, "Phe duyet tu button Telegram", cancellationToken);
            await AnswerCallbackQueryAsync(update.CallbackQueryId, "Da cap nhat trang thai.", cancellationToken);
            await SendMessageCoreAsync(update.ChatId, result, cancellationToken: cancellationToken);
            return 1;
        }

        if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
        {
            var result = await ReviewViolationFromTelegramAsync(trackingId, "Rejected", update, "Tu choi tu button Telegram", cancellationToken);
            await AnswerCallbackQueryAsync(update.CallbackQueryId, "Da cap nhat trang thai.", cancellationToken);
            await SendMessageCoreAsync(update.ChatId, result, cancellationToken: cancellationToken);
            return 1;
        }

        await AnswerCallbackQueryAsync(update.CallbackQueryId, "Hanh dong khong duoc ho tro.", cancellationToken);
        return 1;
    }

    private async Task<string> ReviewViolationFromTelegramAsync(
        string trackingId,
        string status,
        TelegramChatUpdate update,
        string reviewNote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackingId))
        {
            return "Thieu ma vi pham. Dung /pending hoac /latest de lay danh sach.";
        }

        using var scope = _scopeFactory.CreateScope();
        var violationService = scope.ServiceProvider.GetRequiredService<IViolationService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();

        var record = await violationService.GetViolationByTrackingIdAsync(trackingId, cancellationToken);
        if (record == null)
        {
            return $"Khong tim thay vi pham {trackingId}.";
        }

        if (string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            return $"Vi pham {trackingId} da o trang thai {status}.";
        }

        var reviewer = string.IsNullOrWhiteSpace(update.SenderUsername)
            ? update.SenderName
            : $"{update.SenderName} (@{update.SenderUsername})".Trim();

        var success = await violationService.ReviewViolationByTrackingIdAsync(
            trackingId,
            status,
            reviewer,
            "Telegram",
            reviewNote,
            cancellationToken);

        if (!success)
        {
            return $"Khong the cap nhat vi pham {trackingId}.";
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Username = reviewer,
            Action = "Duyệt vi phạm",
            Details = $"Telegram cập nhật {trackingId} sang trạng thái {status}. Ghi chú: {reviewNote}",
            IpAddress = "Telegram",
            Status = "Thành công"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return $"Da cap nhat vi pham {trackingId} sang trang thai {status}.";
    }

    private async Task<string> BuildStatusMessageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();

        var todayUtc = DateTime.UtcNow.Date;
        var totalToday = await dbContext.ViolationRecords.CountAsync(v => v.DetectedAtUtc >= todayUtc, cancellationToken);
        var pending = await dbContext.ViolationRecords.CountAsync(v => v.Status == "Pending", cancellationToken);
        var approved = await dbContext.ViolationRecords.CountAsync(v => v.Status == "Approved", cancellationToken);
        var rejected = await dbContext.ViolationRecords.CountAsync(v => v.Status == "Rejected", cancellationToken);

        return $"Thong ke hom nay:\n- Tong vi pham: {totalToday}\n- Cho duyet: {pending}\n- Da duyet: {approved}\n- Tu choi: {rejected}";
    }

    private async Task<string> BuildPendingMessageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();

        var pending = await dbContext.ViolationRecords
            .Where(v => v.Status == "Pending")
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return "Khong co vi pham nao dang cho duyet.";
        }

        var builder = new StringBuilder("Danh sach vi pham cho duyet:\n");
        foreach (var item in pending)
        {
            builder.AppendLine($"- {item.TrackingId}: {item.ViolationType} | {item.Severity} | {item.DetectedAtUtc:dd/MM HH:mm}");
        }

        builder.Append("Dung /approve <TrackingId> hoac /reject <TrackingId> de cap nhat.");
        return builder.ToString();
    }

    private async Task<string> BuildLatestMessageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();

        var latest = await dbContext.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (latest.Count == 0)
        {
            return "He thong chua ghi nhan vi pham nao.";
        }

        var builder = new StringBuilder("5 vi pham moi nhat:\n");
        foreach (var item in latest)
        {
            builder.AppendLine($"- {item.TrackingId}: {item.ViolationType} | {item.Status} | {item.DetectedAtUtc:dd/MM HH:mm}");
        }

        return builder.ToString();
    }

    private async Task<string> BuildViolationDetailMessageAsync(string trackingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackingId))
        {
            return "Thieu ma vi pham. Dung /violation <TrackingId>.";
        }

        using var scope = _scopeFactory.CreateScope();
        var violationService = scope.ServiceProvider.GetRequiredService<IViolationService>();
        var item = await violationService.GetViolationByTrackingIdAsync(trackingId, cancellationToken);
        if (item == null)
        {
            return $"Khong tim thay vi pham {trackingId}.";
        }

        var reviewedAt = item.ReviewedAtUtc.HasValue
            ? item.ReviewedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            : "Chua duyet";

        return
            $"Chi tiet vi pham {item.TrackingId}\n" +
            $"- Nhan vien: {item.EmployeeName} ({item.EmployeeCode})\n" +
            $"- Loai: {item.ViolationType}\n" +
            $"- Muc do: {item.Severity}\n" +
            $"- Camera: {item.CameraLocation}\n" +
            $"- Trang thai: {item.Status}\n" +
            $"- Ghi nhan: {item.DetectedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}\n" +
            $"- Nguoi duyet: {item.ReviewedBy ?? "Chua cap nhat"}\n" +
            $"- Kenh duyet: {item.ReviewChannel ?? "Chua cap nhat"}\n" +
            $"- Thoi diem duyet: {reviewedAt}";
    }

    private static string BuildHelpMessage()
    {
        return
            "Danh sach lenh Telegram:\n" +
            "/start - Khoi dong bot\n" +
            "/help - Xem huong dan\n" +
            "/chatid - Lay chat id hien tai\n" +
            "/status - Xem thong ke vi pham hom nay\n" +
            "/pending - Xem vi pham dang cho duyet\n" +
            "/latest - Xem 5 vi pham moi nhat\n" +
            "/violation <TrackingId> - Xem chi tiet 1 vi pham\n" +
            "/approve <TrackingId> - Duyet vi pham\n" +
            "/reject <TrackingId> <ghi-chu> - Tu choi vi pham";
    }

    private async Task<IReadOnlyCollection<TelegramChatUpdate>> GetUpdatesCoreAsync(long? offset, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_options.BotToken}/getUpdates";
            if (offset.HasValue)
            {
                url += $"?offset={offset.Value}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram getUpdates failed with status {StatusCode}", response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var updates = new List<TelegramChatUpdate>();
            foreach (var item in resultElement.EnumerateArray())
            {
                if (!item.TryGetProperty("update_id", out var updateIdElement))
                {
                    continue;
                }

                if (item.TryGetProperty("callback_query", out var callbackElement))
                {
                    var callbackUpdate = ParseCallbackUpdate(updateIdElement.GetInt64(), callbackElement);
                    if (callbackUpdate is not null)
                    {
                        updates.Add(callbackUpdate);
                    }
                    continue;
                }

                if (!item.TryGetProperty("message", out var messageElement) && !item.TryGetProperty("channel_post", out messageElement))
                {
                    continue;
                }

                var messageUpdate = ParseMessageUpdate(updateIdElement.GetInt64(), messageElement);
                if (messageUpdate is not null)
                {
                    updates.Add(messageUpdate);
                }
            }

            return updates.OrderByDescending(x => x.MessageUtc).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram getUpdates threw.");
            return [];
        }
    }

    private TelegramChatUpdate? ParseMessageUpdate(long updateId, JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("chat", out var chatElement))
        {
            return null;
        }

        var fromElementExists = messageElement.TryGetProperty("from", out var fromElement);
        var senderName = fromElementExists && fromElement.TryGetProperty("first_name", out var firstNameElement)
            ? firstNameElement.GetString() ?? string.Empty
            : string.Empty;
        var senderLast = fromElementExists && fromElement.TryGetProperty("last_name", out var lastNameElement)
            ? lastNameElement.GetString() ?? string.Empty
            : string.Empty;

        return new TelegramChatUpdate
        {
            UpdateId = updateId,
            ChatId = chatElement.TryGetProperty("id", out var chatIdElement) ? chatIdElement.ToString() : string.Empty,
            ChatType = chatElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty,
            ChatTitle = chatElement.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? string.Empty
                : chatElement.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() ?? string.Empty : string.Empty,
            SenderName = $"{senderName} {senderLast}".Trim(),
            SenderUsername = fromElementExists && fromElement.TryGetProperty("username", out var senderUsernameElement)
                ? senderUsernameElement.GetString() ?? string.Empty
                : string.Empty,
            MessageText = messageElement.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty,
            MessageUtc = messageElement.TryGetProperty("date", out var dateElement)
                ? DateTimeOffset.FromUnixTimeSeconds(dateElement.GetInt64()).UtcDateTime
                : DateTime.UtcNow
        };
    }

    private TelegramChatUpdate? ParseCallbackUpdate(long updateId, JsonElement callbackElement)
    {
        if (!callbackElement.TryGetProperty("message", out var messageElement) || !messageElement.TryGetProperty("chat", out var chatElement))
        {
            return null;
        }

        var fromElementExists = callbackElement.TryGetProperty("from", out var fromElement);
        var senderName = fromElementExists && fromElement.TryGetProperty("first_name", out var firstNameElement)
            ? firstNameElement.GetString() ?? string.Empty
            : string.Empty;
        var senderLast = fromElementExists && fromElement.TryGetProperty("last_name", out var lastNameElement)
            ? lastNameElement.GetString() ?? string.Empty
            : string.Empty;

        return new TelegramChatUpdate
        {
            UpdateId = updateId,
            ChatId = chatElement.TryGetProperty("id", out var chatIdElement) ? chatIdElement.ToString() : string.Empty,
            ChatType = chatElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty,
            ChatTitle = chatElement.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? string.Empty
                : chatElement.TryGetProperty("username", out var usernameElement) ? usernameElement.GetString() ?? string.Empty : string.Empty,
            SenderName = $"{senderName} {senderLast}".Trim(),
            SenderUsername = fromElementExists && fromElement.TryGetProperty("username", out var senderUsernameElement)
                ? senderUsernameElement.GetString() ?? string.Empty
                : string.Empty,
            MessageText = messageElement.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty,
            MessageUtc = callbackElement.TryGetProperty("message", out var callbackMessageElement)
                && callbackMessageElement.TryGetProperty("date", out var dateElement)
                    ? DateTimeOffset.FromUnixTimeSeconds(dateElement.GetInt64()).UtcDateTime
                    : DateTime.UtcNow,
            IsCallbackQuery = true,
            CallbackQueryId = callbackElement.TryGetProperty("id", out var callbackIdElement) ? callbackIdElement.GetString() ?? string.Empty : string.Empty,
            CallbackData = callbackElement.TryGetProperty("data", out var dataElement) ? dataElement.GetString() ?? string.Empty : string.Empty
        };
    }

    private async Task<TelegramSendResult> SendMessageCoreAsync(
        string chatId,
        string message,
        object? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = message
            };

            if (replyMarkup is not null)
            {
                payload["reply_markup"] = replyMarkup;
            }

            var response = await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_options.BotToken}/sendMessage",
                payload,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TelegramSendResult
            {
                Success = response.IsSuccessStatusCode,
                ChatId = chatId,
                Message = message,
                ResponseSummary = $"{(int)response.StatusCode} {response.StatusCode}: {body}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram send threw for chat {ChatId}", chatId);
            return new TelegramSendResult
            {
                Success = false,
                ChatId = chatId,
                Message = message,
                ResponseSummary = ex.Message
            };
        }
    }

    private async Task AnswerCallbackQueryAsync(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callbackQueryId))
        {
            return;
        }

        try
        {
            await _httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_options.BotToken}/answerCallbackQuery",
                new
                {
                    callback_query_id = callbackQueryId,
                    text
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram answerCallbackQuery threw.");
        }
    }

    private List<string> GetTargetChatIds()
    {
        var configured = _options.ChatIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!_options.AllowDynamicChatIds)
        {
            return configured;
        }

        foreach (var dynamicId in _state.GetKnownChatIds())
        {
            if (!configured.Contains(dynamicId, StringComparer.Ordinal))
            {
                configured.Add(dynamicId);
            }
        }

        return configured;
    }

    private string BuildWelcomeMessage(string chatId)
    {
        return $"{_options.WelcomeMessage}\nChat ID cua ban: {chatId}\nGui /help de xem danh sach lenh.";
    }

    private static object BuildViolationInlineKeyboard(ViolationRecord violation)
    {
        return new
        {
            inline_keyboard = new object[]
            {
                new object[]
                {
                    new { text = "Duyet", callback_data = $"violation|approve|{violation.TrackingId}" },
                    new { text = "Tu choi", callback_data = $"violation|reject|{violation.TrackingId}" }
                },
                new object[]
                {
                    new { text = "Chi tiet", callback_data = $"violation|detail|{violation.TrackingId}" }
                }
            }
        };
    }

    private static string BuildViolationNotificationText(ViolationRecord violation, string message)
    {
        return
            $"{message}\n" +
            $"Ma vi pham: {violation.TrackingId}\n" +
            $"Nhan vien: {violation.EmployeeName} ({violation.EmployeeCode})\n" +
            $"Muc do: {violation.Severity}\n" +
            $"Trang thai: {violation.Status}\n" +
            $"Dung /approve {violation.TrackingId} hoac /reject {violation.TrackingId} neu can.";
    }
}
