using System.Windows;
using System.Windows.Interop;

namespace GodjiVpn.Services;

/// <summary>
/// Порт Android TokenManager.recoverWebRefreshToken (955fd69). Вход через сайт (WebLoginWindow)
/// до исправления сохранялся без refresh-токена: кука rw_refresh_token выставлена с
/// Path=/api/auth, а куки читались для корня сайта, где её не видно. Через сутки сессионный JWT
/// истекал, продлить его было нечем — тариф и срок пропадали (реальная жалоба, помог только
/// выход и вход). Сама кука при этом лежит в хранилище WebView2 (общая папка данных,
/// WebView2EnvironmentProvider). Разово забираем её оттуда — только если сессионная кука там та
/// же, что наш текущий токен: значит, это тот же вход, а не старая сессия другого аккаунта.
/// </summary>
public static class WebRefreshTokenRecovery
{
    private const string SiteUrl = "https://gojihub.xyz/";
    private const string RefreshUrl = "https://gojihub.xyz/api/auth/refresh";

    public static async Task TryRecoverAsync(TokenStore tokens, Window owner)
    {
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1") return;
        if (!tokens.IsLoggedIn || tokens.RefreshToken != null) return;
        try
        {
            var env = await WebView2EnvironmentProvider.GetAsync();
            var hwnd = new WindowInteropHelper(owner).EnsureHandle();
            // Невидимый контроллер — только чтобы добраться до хранилища кук, страница не грузится.
            var controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
            try
            {
                controller.IsVisible = false;
                var cookies = controller.CoreWebView2.CookieManager;
                var session = (await cookies.GetCookiesAsync(SiteUrl)).FirstOrDefault(c => c.Name == "rw_session_token")?.Value;
                var refresh = (await cookies.GetCookiesAsync(RefreshUrl)).FirstOrDefault(c => c.Name == "rw_refresh_token")?.Value;
                if (session == tokens.AccessToken && !string.IsNullOrWhiteSpace(refresh))
                    tokens.SaveRefreshToken(refresh);
            }
            finally
            {
                controller.Close();
            }
        }
        catch
        {
            // Нет WebView2 Runtime или хранилища — сессия просто проживёт до истечения, как раньше.
        }
    }
}
