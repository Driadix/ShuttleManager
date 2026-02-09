using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.TcpOfClient;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();
await builder.Build().RunAsync();
