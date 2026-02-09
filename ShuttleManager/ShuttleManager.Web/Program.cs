using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.TcpOfClient;
using ShuttleManager.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Add device-specific services used by the ShuttleManager.Shared project
builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(ShuttleManager.Shared._Imports).Assembly,
        typeof(ShuttleManager.Web.Client._Imports).Assembly);

app.Run();
