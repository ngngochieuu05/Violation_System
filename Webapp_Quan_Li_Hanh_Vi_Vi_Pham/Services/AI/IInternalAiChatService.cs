using System.Security.Claims;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.AI;

public interface IInternalAiChatService
{
    Task<InternalChatResponse> AskAsync(
        ClaimsPrincipal principal,
        InternalChatRequest request,
        CancellationToken cancellationToken = default);
}
