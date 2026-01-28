using Microsoft.Extensions.Logging;
using ShuttleManager.Platforms.Windows.Services;
using ShuttleManager.Services;
using ShuttleManager.Shared.Interfaces;
using ShuttleManager.Shared.Services;
namespace ShuttleManager.Platforms.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");


            });

        builder.Services.AddSingleton<ITcpClientService, TcpClientService>();
        builder.Services.AddSingleton<IShuttleHubClientService, ShuttleHubClientService>();
        builder.Services.AddSingleton<IBrowserLauncherService, BrowserLauncherService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IWebBrowserService, WebBrowserService>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
        #endif

        return builder.Build();
    }

}
