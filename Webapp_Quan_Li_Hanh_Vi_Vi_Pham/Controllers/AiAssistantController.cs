using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.AI;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[Authorize(Roles = "Employee,Manager")]
[ApiController]
[Route("api/ai-assistant")]
public class AiAssistantController : ControllerBase
{
    private readonly IInternalAiChatService _chatService;

    public AiAssistantController(IInternalAiChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] InternalChatRequest request, CancellationToken cancellationToken)
    {
        var response = await _chatService.AskAsync(User, request, cancellationToken);
        return Ok(response);
    }
}
