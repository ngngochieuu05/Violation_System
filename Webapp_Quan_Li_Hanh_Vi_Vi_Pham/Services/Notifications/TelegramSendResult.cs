namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramSendResult
{
    public bool Success { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ResponseSummary { get; set; } = string.Empty;
}
