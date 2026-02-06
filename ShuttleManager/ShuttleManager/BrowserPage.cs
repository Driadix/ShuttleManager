using ShuttleManager.Services;

namespace ShuttleManager;

public partial class BrowserPage : ContentPage
{
    // Статический кэш: ключ = стартовый URL, значение = последний посещённый URL
    private static readonly Dictionary<string, string> _urlCache = new();

    private readonly WebView _webView;
    private readonly ActivityIndicator _loading;
    private readonly string _startUrl; // Сохраняем стартовый URL для кэширования
    private string _currentUrl;

    public BrowserPage(string url)
    {
        

        _startUrl = url; // Запоминаем стартовый URL
        _currentUrl = url;

        // Если есть сохранённый URL — используем его
        if (_urlCache.TryGetValue(url, out var cachedUrl))
        {
            _currentUrl = cachedUrl;
            url = cachedUrl;
        }

        Title = "Firmware Update";

        _webView = new WebView
        {
            Source = new UrlWebViewSource { Url = url },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _loading = new ActivityIndicator
        {
            IsVisible = true,
            IsRunning = true,
            Color = Colors.Blue,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _webView.Navigating += (_, __) =>
        {
            _loading.IsVisible = true;
            _loading.IsRunning = true;
        };

        _webView.Navigated += (_, e) =>
        {
            _currentUrl = e.Url; // ← ВОТ КЛЮЧЕВОЙ МОМЕНТ
            _loading.IsRunning = false;
            _loading.IsVisible = false;
        };

        var backButton = new Button
        {
            Text = "←",
            FontSize = 18,
            BackgroundColor = Colors.Transparent
        };

        backButton.Clicked += (_, _) =>
        {
            if (_webView.CanGoBack)
                _webView.GoBack();
        };

        var reloadButton = new Button
        {
            Text = "⟳",
            FontSize = 18,
            BackgroundColor = Colors.Transparent
        };

        reloadButton.Clicked += (_, _) => _webView.Reload();

        var minimizeButton = new Button
        {
            Text = "—",
            FontSize = 18,
            BackgroundColor = Colors.Transparent
        };

        minimizeButton.Clicked += async (_, _) =>
        {
            // Сохраняем ТЕКУЩИЙ URL (не из Source, а из поля)
            _urlCache[_startUrl] = _currentUrl;

            if (Navigation?.ModalStack?.Any() == true)
                await Navigation.PopModalAsync(animated: true);
        };

        var homeButton = new Button
        {
            Text = "⌂",
            FontSize = 18,
            BackgroundColor = Colors.Transparent
        };

        homeButton.Clicked += async (_, _) =>
        {
            _webView.Source = new UrlWebViewSource { Url = _startUrl };
        };

        var toolbar = new Grid
        {
            Padding = new Thickness(8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            BackgroundColor = Colors.LightGray
        };

        toolbar.Add(backButton, 0);
        toolbar.Add(reloadButton, 1);
        toolbar.Add(homeButton,2);
        toolbar.Add(minimizeButton, 3);

        var contentGrid = new Grid
        {
            Children =
            {
                _webView,
                _loading
            }
        };

        var rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        rootGrid.Add(toolbar, 0, 0);
        rootGrid.Add(contentGrid, 0, 1);

        Content = rootGrid;
    }
}