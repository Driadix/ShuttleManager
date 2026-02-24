using Microsoft.UI;
using Microsoft.UI.Xaml;
using ShuttleManager.Platforms.Windows;
using System.Diagnostics;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ShuttleManager.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Попытка перехватить необработанные исключения
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                Console.WriteLine($"[CRITICAL] Unhandled Exception: {exception?.Message}");
                // Здесь можно записать ошибку в файл или отправить в аналитику
            };

            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            var window = (Application.Windows.FirstOrDefault() as Microsoft.Maui.Controls.Window);
            if (window != null)
            {
                window.Title = "[MICRON] Менеджер шаттлов S.V.3.1";

                var nativeWindow = window.Handler.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow != null)
                {
                    var hWnd = WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                    if (appWindow != null)
                    {
                        appWindow.Title = "[MICRON] Менеджер шаттлов";
                        // Устанавливаем размер окна
                        //appWindow.Resize(new Windows.Graphics.SizeInt32(3840, 2160));
                        //appWindow.Resize(new Windows.Graphics.SizeInt32(1920, 1080));

                        // Отключаем возможность изменения размера
                        var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                        if (presenter != null)
                        {
                            presenter.IsResizable = false;
                            presenter.IsMaximizable = false;
                            presenter.IsMinimizable = true;
                        }
                    }
                }
            }
        }
    }
}