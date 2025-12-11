using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShuttleManager.Shared.Intefraces;
using ShuttleManager.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<INetworkDeviceScanner, NetworkDeviceScanner>();
builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();

await builder.Build().RunAsync();
