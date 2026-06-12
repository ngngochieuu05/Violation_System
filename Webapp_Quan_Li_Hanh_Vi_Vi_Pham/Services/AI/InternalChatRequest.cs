namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.AI;

public class InternalChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<InternalChatMessage> History { get; set; } = [];
}
