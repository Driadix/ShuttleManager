namespace ShuttleManager.Shared.Services.WebBrowser;

public interface IWebBrowserService
{
    Task OpenWebViewBrowser(string url);

    Task OpenBrowserAsync(Uri uri);
}
