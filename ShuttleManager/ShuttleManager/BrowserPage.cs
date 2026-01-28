namespace ShuttleManager;

public class BrowserPage : ContentPage
{
    private readonly WebView _webView;
    private readonly ActivityIndicator _loading;

    public BrowserPage(string url)
    {
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

        _webView.Navigated += (_, __) =>
        {
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

        var closeButton = new Button
        {
            Text = "✕",
            FontSize = 18,
            BackgroundColor = Colors.Transparent
        };

        closeButton.Clicked += async (_, _) =>
        {
            if (Navigation?.ModalStack?.Any() == true)
                await Navigation.PopModalAsync(animated: true);
        };

        var toolbar = new Grid
        {
            Padding = new Thickness(8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            BackgroundColor = Colors.LightGray
        };

        toolbar.Add(backButton, 0, 0);
        toolbar.Add(reloadButton, 1, 0);
        toolbar.Add(closeButton, 3, 0);

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