using Broadcaster.Hubs;

Console.WriteLine("=================================");
Console.WriteLine("Broadcaster Server Starting...");
Console.WriteLine("=================================");

var builder = WebApplication.CreateBuilder(args);

// Настройка Kestrel для прослушивания на всех IP
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(7717); // Порт 7717
});

// Добавление SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = null; // Без ограничения размера сообщений
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
});

// Добавление CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseRouting();

// Подключение Hub
app.MapHub<BroadcasterHub>("/livehub");

Console.WriteLine("✓ SignalR server started successfully!");
Console.WriteLine("✓ Listening on http://*:7717");
Console.WriteLine("✓ Hub path: /livehub");
Console.WriteLine("=================================");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine("=================================");

app.Run();