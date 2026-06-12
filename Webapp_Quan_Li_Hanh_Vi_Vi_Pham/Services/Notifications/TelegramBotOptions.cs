namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Notifications;

public class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;
    public List<string> ChatIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public bool AllowDynamicChatIds { get; set; } = true;
    public int CommandPollingIntervalSeconds { get; set; } = 5;
    public string WelcomeMessage { get; set; } =
        "Xin chào. Bot cảnh báo vi phạm đã sẵn sàng. Hệ thống sẽ tự động gửi cảnh báo khi phát hiện hút thuốc hoặc rời vị trí làm việc.";
}
