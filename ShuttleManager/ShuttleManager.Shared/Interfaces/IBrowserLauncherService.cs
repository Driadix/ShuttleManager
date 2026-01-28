namespace ShuttleManager.Shared.Interfaces;

public interface IBrowserLauncherService
{
    Task OpenBrowserAsync(Uri uri);
}


