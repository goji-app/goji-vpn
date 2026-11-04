using System.IO;
using Microsoft.Web.WebView2.Core;

namespace GodjiVpn.Services;

/// <summary>
/// Общий CoreWebView2Environment на весь процесс — переиспользуется и глобусом (Controls/
/// GlobeHost.xaml.cs), и окном веб-входа (Views/WebLoginWindow.xaml.cs). По умолчанию WebView2
/// создаёт свою папку данных (EBWebView) рядом с exe — при установке в Program Files туда нет
/// прав на запись даже под администратором (виртуализация/ACL Program Files), поэтому явно
/// указываем UserDataFolder в %LOCALAPPDATA%. Один и тот же Environment для нескольких
/// WebView2-контролов в одном процессе — штатный, рекомендованный Microsoft способ (экономит
/// ресурсы браузерного процесса), а не просто с одной и той же папкой на каждый.
/// </summary>
public static class WebView2EnvironmentProvider
{
    private static Task<CoreWebView2Environment>? _task;

    public static Task<CoreWebView2Environment> GetAsync()
    {
        // Неудачная попытка не кэшируется: следующий вызов (повторное открытие окна веб-входа,
        // глобуса и т.п.) создаёт среду заново, а не вечно возвращает тот же faulted-таск.
        if (_task == null || _task.IsCanceled || (_task.IsCompleted && _task.IsFaulted))
            _task = CreateAsync();
        return _task;
    }

    /// <summary>Сбросить кэш среды — путь восстановления GlobeHost после смерти браузерного
    /// процесса (BrowserProcessExited): среда, пережившая смерть своего процесса, может не
    /// поднять новый контрол, поэтому пробуем ещё раз с заведомо свежей (новая среда с той же
    /// папкой данных в одном процессе — штатный сценарий, браузерный процесс у них общий).</summary>
    public static void Reset() => _task = null;

    private static Task<CoreWebView2Environment> CreateAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GodjiVpn", "WebView2");
        return CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }
}
