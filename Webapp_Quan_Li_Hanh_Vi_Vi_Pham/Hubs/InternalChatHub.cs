using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Hubs;

[Authorize]
public class InternalChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(username))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BuildUsernameGroup(username));
        }

        var userId = Context.User?.FindFirst("UserId")?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BuildUserIdGroup(userId));
        }

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.IsNullOrWhiteSpace(role))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role.ToLowerInvariant()}");
        }

        await base.OnConnectedAsync();
    }

    public static string BuildUsernameGroup(string username) => $"user:{username.Trim().ToLowerInvariant()}";

    public static string BuildUserIdGroup(string userId) => $"userid:{userId.Trim().ToLowerInvariant()}";
}
