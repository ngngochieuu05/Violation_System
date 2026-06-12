namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.AI;

public class GeminiChatOptions
{
    public const string SectionName = "GeminiChat";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public bool Enabled { get; set; } = true;
}
