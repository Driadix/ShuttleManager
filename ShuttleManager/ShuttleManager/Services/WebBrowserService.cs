using Microsoft.Maui.Controls.PlatformConfiguration;
using ShuttleManager.Shared.Services.WebBrowser;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShuttleManager.Services;

public class WebBrowserService : IWebBrowserService
{
    public static WebBrowserService Instance { get; private set; } = new WebBrowserService();

    public WebBrowserService()
    {
        Instance = this;
    }

    public async Task OpenWebViewBrowser(string url)
    {
        var page = new BrowserPage(url);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.Windows[0].Page.Navigation.PushModalAsync(page);
        });
    }

    public async Task MinimizeBrowser()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.Windows[0]?.Navigation.ModalStack?.Any() == true)
            {
                await Application.Current.Windows[0].Page.Navigation.PopModalAsync(animated: true);
            }
        });
    }


    public async Task OpenBrowserAsync(Uri uri)
    {
        if (uri == null)
        {
            Console.WriteLine("[DesktopBrowserLauncherService] URI is null.");
            return;
        }
        string url = uri.ToString();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Console.WriteLine($"[DesktopBrowserLauncherService] Unsupported OS platform for opening browser: {RuntimeInformation.OSDescription}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DesktopBrowserLauncherService] Error opening browser: {ex.Message}");
        }
    }
}

