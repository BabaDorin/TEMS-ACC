using CommunicationLibrary.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicationLibrary.Communication
{
    public sealed class Communicator
    {
        private static readonly Lazy<Communicator> instance = new(() => new Communicator());

        public delegate void SignalRReconnecting(bool connected);
        public event SignalRReconnecting? Reconnecting;
        public event SignalRReconnecting? ConnectionLost;

        private HubConnection? _connection;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private bool _isReconnecting = false;

        static Communicator() { }

        private Communicator()
        {
            InitializeConnection();
        }

        private void InitializeConnection()
        {
            // Чтение конфигурации
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            string ip = config["BroadcasterIP"] ?? "127.0.0.1";
            string port = config["BroadcasterPort"] ?? "7717";
            string url = $"http://{ip}:{port}/livehub";

            Console.WriteLine($"Connecting to: {url}");

            _connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect(new[] {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10)
                })
                .Build();

            _connection.Closed += async (error) =>
            {
                Console.WriteLine($"Connection closed: {error?.Message}");
                ConnectionLost?.Invoke(false);
                await TryReconnect();
            };

            _connection.Reconnecting += (error) =>
            {
                Console.WriteLine($"Reconnecting: {error?.Message}");
                Reconnecting?.Invoke(true);
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                Console.WriteLine($"Reconnected: {connectionId}");
                Reconnecting?.Invoke(false);
                return Task.CompletedTask;
            };

            _ = TryConnect();
        }

        private async Task TryReconnect()
        {
            await _reconnectLock.WaitAsync();
            try
            {
                if (_isReconnecting) return;
                _isReconnecting = true;

                while (_connection?.State != HubConnectionState.Connected)
                {
                    try
                    {
                        Console.WriteLine("Attempting to reconnect...");
                        await Task.Delay(4000);
                        await _connection!.StartAsync();
                        Console.WriteLine("Reconnected successfully!");
                        _isReconnecting = false;
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Reconnect failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private async Task TryConnect()
        {
            while (_connection?.State != HubConnectionState.Connected)
            {
                try
                {
                    Console.WriteLine("Connecting to broadcaster...");
                    await _connection!.StartAsync();
                    Console.WriteLine($"Connected! ConnectionId: {_connection.ConnectionId}");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connection failed: {ex.Message}");
                    await Task.Delay(4000);
                }
            }
        }

        public static Communicator Instance => instance.Value;

        // Методы для вызова серверных методов
        public async Task RegisterClient(string hostId)
        {
            if (_connection?.State != HubConnectionState.Connected)
                await TryConnect();

            try
            {
                await _connection!.InvokeAsync("RegisterClient", _connection.ConnectionId, hostId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RegisterClient error: {ex.Message}");
            }
        }

        public async Task TryConnect(string id, string password, string hostId)
        {
            if (_connection?.State != HubConnectionState.Connected)
                await TryConnect();

            try
            {
                await _connection!.InvokeAsync("TryConnect", id, password, hostId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryConnect error: {ex.Message}");
            }
        }

        public void ReadyToConnect(
            Action<bool> clientConnected,
            Action<string, string, string> tryConnect,
            Action<string> authenticateSuccess,
            Action<InputDataComm> produced,
            Action stopScreenShare,
            Action requestToResub)
        {
            _connection?.On("ClientConnected", clientConnected);
            _connection?.On("TryConnect", tryConnect);
            _connection?.On("AuthenticateSuccess", authenticateSuccess);
            _connection?.On("Produce", produced);
            _connection?.On("StopScreenShare", stopScreenShare);
            _connection?.On("RequestToReSub", requestToResub);
        }

        public async Task AuthenticateSuccess(string clientId, string hostId)
        {
            try
            {
                await _connection!.InvokeAsync("AuthenticateSuccess", clientId, hostId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AuthenticateSuccess error: {ex.Message}");
            }
        }

        public async Task Produce(InputDataComm broadcastDataComm, string connectedClient)
        {
            try
            {
                await _connection!.InvokeAsync("Produce", broadcastDataComm, connectedClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Produce error: {ex.Message}");
            }
        }

        public async Task ProduceMouseMove(int x, int y, string connectedClient)
        {
            try
            {
                await _connection!.InvokeAsync("ProduceMouseMove", x, y, connectedClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProduceMouseMove error: {ex.Message}");
            }
        }

        public async Task StopScreenShare(string hostId)
        {
            try
            {
                await _connection!.InvokeAsync("StopScreenShare", hostId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StopScreenShare error: {ex.Message}");
            }
        }

        public void ReadyToReceiveInput(
            Action<int, int> mouseMoved,
            Action<byte[], string, string> screenshotReceived)
        {
            _connection?.On("ProduceMouseMove", mouseMoved);
            _connection?.On("ProduceScreenshot", screenshotReceived);
        }

        public async Task ProduceScreenshot(byte[] image, int width, int height, string connectedClient)
        {
            try
            {
                await _connection!.InvokeAsync("ProduceScreenshot", image, width.ToString(), height.ToString(), connectedClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProduceScreenshot error: {ex.Message}");
            }
        }

        public async Task Disconnect(string hostId)
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                await _connection.StopAsync();
            }
        }
    }
}