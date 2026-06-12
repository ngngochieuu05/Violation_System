using System.Collections.Concurrent;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramBotState
{
    private readonly ConcurrentDictionary<string, byte> _knownChatIds = new(StringComparer.Ordinal);

    public long LastProcessedUpdateId { get; set; }

    public void RememberChatId(string chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId))
        {
            _knownChatIds.TryAdd(chatId, 0);
        }
    }

    public IReadOnlyCollection<string> GetKnownChatIds()
    {
        return _knownChatIds.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToList();
    }
}
