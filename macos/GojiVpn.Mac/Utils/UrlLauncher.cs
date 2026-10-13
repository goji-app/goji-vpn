using System.Diagnostics;

namespace GodjiVpn.Utils;

/// <summary>Единая точка открытия внешних ссылок (Process.Start с UseShellExecute=true) — часть
/// URL, которые сюда попадают, приходит с бэкенда как есть (текст новости/FAQ через RichContent,
/// кнопки рассылок), не только из захардкоженных констант вроде ссылок на условия использования.
/// UseShellExecute=true передаёт строку напрямую в ShellExecuteEx — без проверки схемы это
/// позволило бы скомпрометированному бэкенду (или испорченной новости) открыть произвольный
/// зарегистрированный в системе URI-протокол, включая file:/UNC-путь (\\host\share — классический
/// способ слить NTLM-хэш машины на подконтрольный SMB-listener одним кликом по "ссылке") или
/// протокол стороннего приложения с собственной уязвимостью в обработчике. Ограничиваем набор
/// схем до тех, что реально нужны (http/https — сайт/оплата/соцсети, mailto — "написать
/// разработчику"), остальное молча не открываем, а не пытаемся отфильтровать блок-лист (его
/// всегда можно обойти новым необычным URI-scheme).</summary>
public static class UrlLauncher
{
    private static readonly string[] AllowedSchemes = { "https", "http", "mailto" };

    /// <returns>false — схема не в списке разрешённых (ссылка не открыта) или запуск не
    /// удался (нет обработчика схемы, битый URL и т.п.).</returns>
    public static bool TryOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (Array.IndexOf(AllowedSchemes, uri.Scheme) < 0) return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
