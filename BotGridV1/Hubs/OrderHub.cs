using Microsoft.AspNetCore.SignalR;

namespace BotGridV1.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time order updates
    /// </summary>
    public class OrderHub : Hub
    {
        public async Task JoinOrderGroup(string configId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"orders_{configId}");
        }

        public async Task LeaveOrderGroup(string configId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"orders_{configId}");
        }
    }
}

