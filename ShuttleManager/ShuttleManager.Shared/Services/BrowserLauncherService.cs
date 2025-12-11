using ShuttleManager.Shared.Intefraces;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ShuttleManager.Shared.Services;
public class BrowserLauncherService : IBrowserLauncherService
{
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


