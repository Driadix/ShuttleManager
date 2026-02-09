using Microsoft.Extensions.Logging;
using ShuttleManager.Services;
using ShuttleManager.Shared.Services.FilePicker;
using ShuttleManager.Shared.Services.ShuttleClient;
using ShuttleManager.Shared.Services.TcpOfClient;
using ShuttleManager.Shared.Services.WebBrowser;

namespace ShuttleManager.Platforms.Windows;

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
