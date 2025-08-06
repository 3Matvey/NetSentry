var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// DI: регистрация сервисов и зависимостей
builder.Services.AddSingleton<NetSentry.Crypto.ICryptoProvider, NetSentry.Crypto.CryptoProvider>();
builder.Services.AddSingleton<NetSentry.Drivers.ITunAdapter, NetSentry.Drivers.TunAdapter>();
#if WINDOWS
builder.Services.AddSingleton<NetSentry.Routing.IRouteManager, NetSentry.Routing.WindowsRouteManager>();
#else
builder.Services.AddSingleton<NetSentry.Routing.IRouteManager, NetSentry.Routing.LinuxRouteManager>();
#endif
builder.Services.AddSingleton<NetSentry.Framing.IFramer, NetSentry.Framing.AdvancedFramer>();


var config = builder.Configuration;
int listenPort = config.GetValue<int>("UdpTransport:ListenPort");

builder.Services.AddSingleton<NetSentry.Network.IUdpTransport>(sp =>
    new NetSentry.Network.UdpTransport(listenPort)); builder.Services.AddSingleton<NetSentry.Services.ITunnelService, NetSentry.Services.TunnelService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
