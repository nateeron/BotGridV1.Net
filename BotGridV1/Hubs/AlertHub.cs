using Microsoft.AspNetCore.SignalR;

namespace BotGridV1.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time alert and log updates
    /// </summary>
    public class AlertHub : Hub
    {
        /// <summary>
        /// Join alert group for a specific config ID
        /// </summary>
        public async Task JoinAlertGroup(string configId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"alerts_{configId}");
        }

        /// <summary>
        /// Leave alert group for a specific config ID
        /// </summary>
        public async Task LeaveAlertGroup(string configId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"alerts_{configId}");
        }

        /// <summary>
        /// Join all alerts group
        /// </summary>
        public async Task JoinAllAlerts()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "all_alerts");
        }
    }
}

