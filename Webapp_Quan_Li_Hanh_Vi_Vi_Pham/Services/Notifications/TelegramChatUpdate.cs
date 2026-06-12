namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramChatUpdate
{
    public long UpdateId { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string ChatType { get; set; } = string.Empty;
    public string ChatTitle { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public bool IsCallbackQuery { get; set; }
    public string CallbackQueryId { get; set; } = string.Empty;
    public string CallbackData { get; set; } = string.Empty;
    public DateTime MessageUtc { get; set; }
}
