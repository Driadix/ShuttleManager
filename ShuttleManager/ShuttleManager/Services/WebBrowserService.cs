using ShuttleManager.Shared.Interfaces;

namespace ShuttleManager.Services;

public class WebBrowserService : IWebBrowserService
{
    public static WebBrowserService Instance { get; private set; }

    public WebBrowserService()
    {
        Instance = this;
    }

    public async Task OpenWebViewBrowser(string url)
    {
        var page = new BrowserPage(url); // ← 1 параметр, как сейчас
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Application.Current.MainPage.Navigation.PushModalAsync(page);
        });
    }

    public async Task MinimizeBrowser()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage?.Navigation.ModalStack?.Any() == true)
            {
                await Application.Current.MainPage.Navigation.PopModalAsync(animated: true);
            }
        });
    }
}

