namespace ShuttleManager.Shared.Intefraces;

public interface IBrowserLauncherService
{
    /// <summary>
    /// Пытается открыть указанный URI (URL) во внешнем браузере.
    /// </summary>
    /// <param name="uri">URI для открытия (например, https://example.com).</param>
    /// <returns>Задача, представляющая асинхронную операцию.</returns>
    Task OpenBrowserAsync(Uri uri);
}


