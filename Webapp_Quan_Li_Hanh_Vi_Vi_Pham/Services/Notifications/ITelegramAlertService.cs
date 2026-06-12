using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public interface ITelegramAlertService
{
    Task SendAlertAsync(string message, CancellationToken cancellationToken = default);
    Task SendViolationAlertAsync(ViolationRecord violation, string message, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TelegramChatUpdate>> GetRecentUpdatesAsync(CancellationToken cancellationToken = default);
    Task<TelegramSendResult> SendTestMessageAsync(string message, string? chatId = null, CancellationToken cancellationToken = default);
    Task<int> ProcessPendingUpdatesAsync(CancellationToken cancellationToken = default);
    IReadOnlyCollection<string> GetKnownChatIds();
}
