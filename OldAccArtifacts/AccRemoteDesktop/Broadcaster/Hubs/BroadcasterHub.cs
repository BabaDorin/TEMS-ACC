using CommunicationLibrary.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Broadcaster.Hubs
{
    public class BroadcasterHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> ConnectedClients = new();

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            string clientId = Context.ConnectionId;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client disconnected: {clientId}");

            // Удаление из словаря всех записей с этим ConnectionId
            var keysToRemove = ConnectedClients
                .Where(pair => pair.Value == clientId)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                ConnectedClients.TryRemove(key, out _);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Removed mapping: {key} -> {clientId}");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // OnReconnectedAsync не существует в ASP.NET Core SignalR
        // Используется автоматическое переподключение на клиенте

        // Методы Hub
        public async Task RegisterClient(string hubId, string screenShareId)
        {
            ConnectedClients.TryAdd(screenShareId, hubId);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Registered: {screenShareId} -> {hubId}");

            await Clients.Client(hubId).SendAsync("ClientConnected", true);
        }

        public async Task TryConnect(string username, string password, string hostId)
        {
            if (ConnectedClients.TryGetValue(username, out var connectionId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TryConnect: {hostId} -> {username}");
                await Clients.Client(connectionId).SendAsync("TryConnect", username, password, hostId);
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TryConnect failed: {username} not found");
            }
        }

        public async Task AuthenticateSuccess(string clientId, string hostId)
        {
            if (ConnectedClients.TryGetValue(clientId, out var connectionId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] AuthSuccess: {clientId} <-> {hostId}");
                await Clients.Client(connectionId).SendAsync("AuthenticateSuccess", hostId);
            }
        }

        public async Task Produce(InputDataComm data, string clientId)
        {
            if (ConnectedClients.TryGetValue(clientId, out var connectionId))
            {
                await Clients.Client(connectionId).SendAsync("Produce", data);
            }
        }

        public async Task ProduceMouseMove(int x, int y, string clientId)
        {
            if (ConnectedClients.TryGetValue(clientId, out var connectionId))
            {
                await Clients.Client(connectionId).SendAsync("ProduceMouseMove", x, y);
            }
        }

        public async Task ProduceScreenshot(byte[] data, string width, string height, string clientId)
        {
            if (ConnectedClients.TryGetValue(clientId, out var connectionId))
            {
                //// Отправка асинхронно без блокировки
                //_ = Task.Run(async () =>
                {
                    try
                    {
                        await Clients.Client(connectionId).SendAsync("ProduceScreenshot", data, width, height);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Screenshot send error: {ex.Message}");
                    }
                }//);
            }
        }

        public async Task StopScreenShare(string hostId)
        {
            if (ConnectedClients.TryGetValue(hostId, out var connectionId))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] StopScreenShare: {hostId}");
                await Clients.Client(connectionId).SendAsync("StopScreenShare");
            }
        }
    }
}