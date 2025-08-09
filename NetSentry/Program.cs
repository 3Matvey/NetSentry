using NetSentry.Crypto;
using NetSentry.Drivers;
using NetSentry.Drivers.Linux;
using NetSentry.Drivers.Windows;
using NetSentry.Framing;
using NetSentry.Network;
using NetSentry.Routing;
using NetSentry.Services;
using NetSentry.Shared.Platform;

var builder = WebApplication.CreateBuilder(args);

// === MVC + OpenAPI ===
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// === Общие зависимости ===
builder.Services.AddSingleton<IPlatformInfo, PlatformInfo>();
builder.Services.AddSingleton<ICryptoProvider, CryptoProvider>();
builder.Services.AddSingleton<IFramer, AdvancedFramer>();

// === Платформозависимые реализации ===
var platform = new PlatformInfo().Platform;

if (platform == PlatformType.Windows)
{
    builder.Services.AddSingleton<IRouteManager, WindowsRouteManager>();
    builder.Services.AddSingleton<TunAdapter, WindowsTunAdapter>();
}
else if (platform == PlatformType.Linux)
{
    builder.Services.AddSingleton<IRouteManager, LinuxRouteManager>();
    builder.Services.AddSingleton<TunAdapter, LinuxTunAdapter>();
}
else
{
    throw new PlatformNotSupportedException($"Unsupported platform: {platform}");
}

// === UDP-транспорт ===
int listenPort = builder.Configuration.GetValue<int?>("UdpTransport:ListenPort") ?? 51888;
builder.Services.AddSingleton<IUdpTransport>(_ => new UdpTransport(listenPort));

// === Основной сервис туннелей ===
builder.Services.AddSingleton<ITunnelService, TunnelService>();

var app = builder.Build();

// === HTTP pipeline ===
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
