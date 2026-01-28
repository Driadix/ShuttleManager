using ShuttleManager.Shared.Interfaces;

namespace ShuttleManager.Services
{
    public class WebBrowserService : IWebBrowserService
    {
        public async Task OpenWebViewBrowser(string url)
        {
            var page = new BrowserPage(url);

            var window = Application.Current.Windows.First();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await window.Page.Navigation.PushModalAsync(page);
            });
        }
    }
}
