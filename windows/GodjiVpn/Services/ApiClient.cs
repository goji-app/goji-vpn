using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Клиент реального бэкенда gojihub.xyz — эндпоинты и заголовки сверены напрямую с рабочим
/// Android-кодом (RemnawaveApi.kt/NetworkModule.kt в каноническом источнике), не догадка.
/// host.gojihub.xyz (админка Remnawave) сюда НИКОГДА не подставлять — см. память
/// project-remnawave-backend-architecture.
/// </summary>
public sealed class ApiClient
{
    private const string BaseUrl = "https://gojihub.xyz/";
    private static readonly JsonSerializerOptions JsonOptions = Utils.LenientJson.Options();

    private readonly HttpClient _http;
    // Запасной путь БЕЗ прокси — см. SendWithRefreshAsync: если запрос через туннель падает на
    // уровне соединения (проблема на конкретном exit-узле, а не в самом бэкенде), собственные
    // API-запросы приложения раньше только ждали следующего RefreshAsync, полностью блокируясь
    // временной проблемой узла. Порт "secondary fallback in tunnelAwareProxySelector" из Android
    // (GodjiVpnService.kt) — там это один ProxySelector с несколькими вариантами, тут — второй
    // HttpClient, потому что System.Net.IWebProxy умеет отдать только один адрес на запрос.
    private readonly HttpClient _directHttp;
    private readonly TokenStore _tokenStore;

    public ApiClient(TokenStore tokenStore)
    {
        _tokenStore = tokenStore;
        var handler = new SocketsHttpHandler
        {
            // Пока туннель поднят — гоняем и собственные запросы приложения через локальный
            // SOCKS самого Xray (тот же приём, что tunnelAwareProxySelector() в Android):
            // иначе они шли бы мимо VPN напрямую, что для просмотра тарифов/подписки не
            // критично, но ломает единообразие с тем, что видит сам пользователь через тоннель.
            Proxy = new TunnelAwareProxy(),
            UseProxy = true,
            // По умолчанию true — тогда .NET сам заводит CookieContainer и молча прикладывает
            // все полученные куки к последующим запросам. Нам это не нужно (Authorization
            // выставляем сами через Bearer, см. PrepareRequest) и мешало бы читать нужную нам
            // Set-Cookie как обычный заголовок ответа (см. VerifyOtpAsync) без побочных эффектов.
            UseCookies = false
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        _directHttp = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false }) { BaseAddress = new Uri(BaseUrl) };
        // Дефолтный User-Agent у HttpClient пустой (System.Net.Http без явного значения ничего
        // не шлёт) — заменяем на честный "имя приложения/версия (ОС)", как у обычного
        // десктоп-клиента, а не как у SubscriptionService (там User-Agent намеренно подделан
        // под v2rayNG — это отдельный, узкий случай именно для эндпоинта subs.gojihub.xyz,
        // не трогаем его здесь).
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        foreach (var client in new[] { _http, _directHttp })
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GodjiVPN-Windows", versionText));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({RuntimeInformation.OSDescription})"));
        }
    }

    public async Task<SendOtpResponse> SendOtpAsync(string email, CancellationToken ct = default) =>
        await PostAsync<SendOtpRequest, SendOtpResponse>("api/auth/email/send-otp", new SendOtpRequest { Email = email }, ct).ConfigureAwait(false);

    /// <summary>С бэкенда 7.1.0 сам токен сессии приходит только в Set-Cookie
    /// (rw_session_token), не в теле — см. комментарий у VerifyOtpResponse. Поэтому не обычный
    /// PostAsync (тот отдаёт только десериализованное тело), а раздельно тело + токен из
    /// заголовков ответа.</summary>
    public async Task<(VerifyOtpResponse Body, string Token, string? RefreshToken)> VerifyOtpAsync(string email, string code, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/auth/email/verify-otp")
        {
            Content = JsonContent.Create(new VerifyOtpRequest { Email = email, Code = code }, options: JsonOptions)
        }, ct).ConfigureAwait(false);
        var body = await ReadOrThrowAsync<VerifyOtpResponse>(response, ct).ConfigureAwait(false);
        var token = ExtractCookieValue(response, "rw_session_token")
            ?? throw new InvalidOperationException("В ответе нет куки rw_session_token");
        return (body, token, ExtractCookieValue(response, "rw_refresh_token"));
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct = default) =>
        await GetAsync<MeResponse>("api/auth/me", ct).ConfigureAwait(false);

    public async Task ConsentAsync(CancellationToken ct = default) =>
        await PostAsync<ConsentRequest, object?>("api/auth/consent", new ConsentRequest(), ct).ConfigureAwait(false);

    public async Task<SubscriptionsResponse> GetSubscriptionsAsync(CancellationToken ct = default) =>
        await GetAsync<SubscriptionsResponse>("api/subscriptions", ct).ConfigureAwait(false);

    /// <summary>С бэкенда 7.1.0 трафик убрали из списка (см. SubscriptionInfo.Traffic) — этот
    /// одиночный эндпоинт по-прежнему отдаёт его, дозапрашиваем им при необходимости
    /// (см. SubscriptionRepository.RefreshAsync).</summary>
    public async Task<SubscriptionInfo> GetSubscriptionAsync(long id, CancellationToken ct = default) =>
        await GetAsync<SubscriptionInfo>($"api/subscriptions/{id}", ct).ConfigureAwait(false);

    /// <summary>subscriptionId — веб-версия передаёт его как ?subscription_id=, персональная
    /// скидка (customer_discount_percent) считается бэкендом ИМЕННО относительно конкретной
    /// подписки, а не аккаунта вообще; без него ответ — обобщённый каталог без привязки к
    /// подписке, где то же поле может означать что-то другое (на Android живой тест без
    /// subscription_id вернул 100%, что для обычного тарифа неправдоподобно).</summary>
    public async Task<PlansResponse> GetPlansAsync(long? subscriptionId = null, CancellationToken ct = default) =>
        await GetAsync<PlansResponse>(
            subscriptionId is { } id ? $"api/dashboard/plans?subscription_id={id}" : "api/dashboard/plans",
            ct).ConfigureAwait(false);

    /// <summary>Страница "Мои рассылки"/"Новости" веб-версии (#/my-broadcasts) — список уже
    /// отправленных пользователю новостей/объявлений.</summary>
    public async Task<List<BroadcastDto>> GetBroadcastsAsync(CancellationToken ct = default) =>
        await GetAsync<List<BroadcastDto>>("api/broadcasts/completed", ct).ConfigureAwait(false);

    /// <summary>Страница "Рефералы" веб-версии (#/my-referrals).</summary>
    public async Task<ReferralsResponse> GetReferralsAsync(CancellationToken ct = default) =>
        await GetAsync<ReferralsResponse>("api/dashboard/referrals", ct).ConfigureAwait(false);

    /// <summary>Страница "Партнёрская программа" веб-версии (#/partner-dashboard) — только
    /// сводка/статус, см. PartnerStatusResponse.</summary>
    public async Task<PartnerStatusResponse> GetPartnerStatusAsync(CancellationToken ct = default) =>
        await GetAsync<PartnerStatusResponse>("api/partner/status", ct).ConfigureAwait(false);

    public async Task<List<DeviceDto>> GetDevicesAsync(long subscriptionId, CancellationToken ct = default) =>
        await GetAsync<List<DeviceDto>>($"api/subscriptions/{subscriptionId}/devices", ct).ConfigureAwait(false);

    public async Task RenameDeviceAsync(long subscriptionId, string hwid, string readableName, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Patch, $"api/subscriptions/{subscriptionId}/devices/{hwid}")
        {
            Content = JsonContent.Create(new RenameDeviceRequest { ReadableName = readableName }, options: JsonOptions)
        }, ct).ConfigureAwait(false);
        await ReadOrThrowAsync<object?>(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteDeviceAsync(long subscriptionId, string hwid, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"api/subscriptions/{subscriptionId}/devices/{hwid}"), ct).ConfigureAwait(false);
        await ReadOrThrowAsync<object?>(response, ct).ConfigureAwait(false);
    }

    // ── Поддержка (api/support/*, api/faq) — см. комментарий у SupportTicketsResponse и т.д.
    //    в ApiModels.cs. GetSupportMessagesAsync/GetSupportQueuesAsync идут через
    //    GetNullableAsync, а не обычный GetAsync — их тело ответа само по себе список (не
    //    объект-обёртка), и Go nil-slice сериализуется как буквальный JSON "null" на пустом
    //    результате; обычный ReadOrThrowAsync принял бы это за "Пустой ответ сервера". ────────

    public async Task<List<SupportTicketDto>> GetSupportTicketsAsync(string status, int limit, int offset, CancellationToken ct = default)
    {
        var response = await GetAsync<SupportTicketsResponse>($"api/support/tickets?status={status}&limit={limit}&offset={offset}", ct).ConfigureAwait(false);
        return response.Tickets ?? new();
    }

    public async Task<SupportTicketDto> GetSupportTicketAsync(long ticketId, CancellationToken ct = default) =>
        await GetAsync<SupportTicketDto>($"api/support/tickets/{ticketId}", ct).ConfigureAwait(false);

    public async Task<List<SupportMessageDto>> GetSupportMessagesAsync(long ticketId, CancellationToken ct = default) =>
        await GetNullableAsync<List<SupportMessageDto>>($"api/support/tickets/{ticketId}/messages", ct).ConfigureAwait(false) ?? new();

    public async Task<SupportTicketDto> CreateSupportTicketAsync(string? subject, string message, long? queueId, CancellationToken ct = default)
    {
        var response = await PostAsync<CreateSupportTicketRequest, CreateSupportTicketResponse>("api/support/tickets",
            new CreateSupportTicketRequest { Subject = subject, Message = message, QueueId = queueId }, ct).ConfigureAwait(false);
        return response.Ticket ?? throw new InvalidOperationException("Пустой ответ сервера");
    }

    public async Task SendSupportMessageAsync(long ticketId, string message, CancellationToken ct = default) =>
        await PostAsync<SendSupportMessageRequest, object?>($"api/support/tickets/{ticketId}/messages",
            new SendSupportMessageRequest { Message = message }, ct).ConfigureAwait(false);

    /// <summary>Тот же эндпоинт, что и SendSupportMessageAsync, но multipart/form-data — простой
    /// одношаговый POST, как у веб-клиента: без отдельного протокола init/finalize и без
    /// постадийного прогресса на файл (сознательно не реализовано даже в исходном Android-
    /// клиенте, см. отчёт по 720f5ff). files — уже прочитанное в память содержимое (десктопные
    /// вложения из чата поддержки некрупные, потоковая передача с диска не нужна).</summary>
    public async Task SendSupportMessageWithFilesAsync(long ticketId, string message,
        IReadOnlyList<(string FileName, byte[] Content, string ContentType)> files, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
        {
            var content = new MultipartFormDataContent { { new StringContent(message), "message" } };
            foreach (var file in files)
            {
                var part = new ByteArrayContent(file.Content);
                part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                content.Add(part, "files", file.FileName);
            }
            return new HttpRequestMessage(HttpMethod.Post, $"api/support/tickets/{ticketId}/messages") { Content = content };
        }, ct).ConfigureAwait(false);
        await ReadOrThrowAsync<object?>(response, ct).ConfigureAwait(false);
    }

    public async Task<SupportTicketLimitResponse> GetSupportTicketLimitAsync(CancellationToken ct = default) =>
        await GetAsync<SupportTicketLimitResponse>("api/support/ticket-limit", ct).ConfigureAwait(false);

    public async Task<List<SupportQueueDto>> GetSupportQueuesAsync(CancellationToken ct = default) =>
        await GetNullableAsync<List<SupportQueueDto>>("api/support/queues", ct).ConfigureAwait(false) ?? new();

    public async Task<FaqResponse> GetFaqAsync(CancellationToken ct = default) =>
        await GetAsync<FaqResponse>("api/faq", ct).ConfigureAwait(false);

    private async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct)
    {
        using var response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct).ConfigureAwait(false);
        return await ReadOrThrowAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    private async Task<TResponse?> GetNullableAsync<TResponse>(string path, CancellationToken ct)
    {
        using var response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct).ConfigureAwait(false);
        return await ReadNullableOrThrowAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TBody, TResponse>(string path, TBody body, CancellationToken ct)
    {
        using var response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        }, ct).ConfigureAwait(false);
        return await ReadOrThrowAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    /// <summary>Аналог provideAuthInterceptor() в NetworkModule.kt — там оба заголовка
    /// (Authorization и X-Requested-With) добавляются БЕЗУСЛОВНО на каждый запрос через общий
    /// OkHttp-interceptor, а не только на POST. Раньше X-Requested-With стоял только в
    /// PostAsync — из-за этого GET api/auth/{provider}/start уходил без него, бэкенд не
    /// сохранял app_redirect для сессии, и после логина в Google/Яндекс браузер просто
    /// показывал обычный сайт (gojihub.xyz/#/stats/overview) вместо редиректа в приложение.</summary>
    private void PrepareRequest(HttpRequestMessage request)
    {
        var token = _tokenStore.AccessToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
    }

    /// <summary>Сессионный JWT бэкенда живёт ровно 24 часа — без обновления пользователя
    /// стабильно выкидывало на логин раз в сутки (жалоба пользователя). Аналог
    /// TokenAuthenticator в Android NetworkModule.kt: на 401 (кроме самих auth-эндпоинтов —
    /// неверный OTP-код тоже может прийти как 401, но это не протухшая сессия) пытаемся
    /// продлить сессию через POST api/auth/refresh по rw_refresh_token и повторяем запрос ОДИН
    /// раз с уже новым токеном. requestFactory — не HttpRequestMessage напрямую (его нельзя
    /// переиспользовать повторно после отправки), а фабрика, пересобирающая тот же запрос.</summary>
    /// <summary>Сессия кабинета кончилась и продлить её не удалось: запрос вернул 401, а
    /// refresh-токена нет или он тоже отвергнут. Раньше такое проходило молча — серверы
    /// продолжали подтягиваться по запасной ссылке, а тариф, срок и трафик просто пропадали
    /// (жалоба пользователя «не подтягивается подписка», помог только выход и вход заново).
    /// Подписчик (App) переводит приложение на экран входа.</summary>
    public event Action? SessionExpired;

    // Продление сессии — строго по одному: при старте несколько запросов уходят параллельно и
    // могут разом получить 401. Если бэкенд ротирует refresh-токен, второе продление старым
    // токеном было бы отвергнуто и ложно выкинуло бы пользователя на экран входа.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private void RaiseSessionExpired(string? usedToken)
    {
        // Без токена 401 — норма (пользователь уже вышел), это не «истекла сессия». А если
        // параллельный запрос за это время уже продлил сессию, этот устаревший 401 не должен
        // выкидывать на экран входа со свежим токеном (порт Android 955fd69).
        var current = _tokenStore.AccessToken;
        if (string.IsNullOrEmpty(current) || current != usedToken) return;
        SessionExpired?.Invoke();
    }

    private async Task<HttpResponseMessage> SendWithRefreshAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var sentToken = _tokenStore.AccessToken;
        var response = await SendWithDirectFallbackAsync(requestFactory, ct).ConfigureAwait(false);

        // RequestUri здесь ещё относительный (например "api/subscriptions") — HttpClient
        // резолвит его в абсолютный только в момент реальной отправки через BaseAddress,
        // .AbsolutePath на ещё не отправленном относительном Uri бросает
        // "This operation is not supported for a relative URI." Берём OriginalString — тот же
        // относительный путь как есть, этого достаточно для проверки префикса.
        bool isAuthEndpoint;
        using (var probe = requestFactory()) isAuthEndpoint = probe.RequestUri!.OriginalString.Contains("api/auth/", StringComparison.Ordinal);
        if (response.StatusCode != HttpStatusCode.Unauthorized || isAuthEndpoint)
            return response;

        bool refreshed;
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Пока ждали своей очереди, сессию уже продлил параллельный запрос — просто повторяем.
            refreshed = _tokenStore.AccessToken != sentToken ||
                        (_tokenStore.RefreshToken is { } refreshToken &&
                         await RefreshSessionAsync(refreshToken, ct).ConfigureAwait(false) == RefreshOutcome.Refreshed);
        }
        finally
        {
            _refreshLock.Release();
        }
        if (!refreshed)
        {
            RaiseSessionExpired(sentToken);
            return response; // не получилось обновить — отдаём исходный 401 как есть
        }

        response.Dispose();
        var retryToken = _tokenStore.AccessToken;
        var retried = await SendWithDirectFallbackAsync(requestFactory, ct).ConfigureAwait(false); // подхватит уже обновлённый _tokenStore.AccessToken
        if (retried.StatusCode == HttpStatusCode.Unauthorized) RaiseSessionExpired(retryToken);
        return retried;
    }

    /// <summary>Порт "secondary fallback in tunnelAwareProxySelector" из Android
    /// (GodjiVpnService.kt): пока туннель поднят, собственные запросы приложения идут через
    /// локальный SOCKS xray (см. TunnelAwareProxy), но если ИМЕННО соединение падает (проблема
    /// на конкретном exit-узле — HttpRequestException, а не обычная ошибка HTTP-статуса из
    /// самого бэкенда), пробуем тот же запрос напрямую, а не блокируем всю функциональность API
    /// до следующего переподключения. Если туннель не поднят — TunnelAwareProxy и так уже отдал
    /// null (прямое соединение), второй попытки не будет: тот же результат гарантирован.</summary>
    private async Task<HttpResponseMessage> SendWithDirectFallbackAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        using var request = requestFactory();
        PrepareRequest(request);
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (VpnEngine.Current?.IsRunning == true)
        {
            using var directRequest = requestFactory();
            PrepareRequest(directRequest);
            return await _directHttp.SendAsync(directRequest, ct).ConfigureAwait(false);
        }
    }

    private enum RefreshOutcome { Refreshed, Rejected }

    /// <summary>Без Authorization (сессия уже мертва) — только Cookie с refresh-токеном, как у
    /// веб-версии сайта. При успехе сохраняет новый access- и (если бэкенд его ротирует)
    /// refresh-токен. Rejected — только когда refresh-токен действительно отвергнут (400/401):
    /// сессия мертва, пора на экран входа. Сбой сети, 409 без новой сессии (сайт считает его
    /// «уже обновлено параллельно»), 429, 5xx и ошибки CDN — временные: бросается
    /// HttpRequestException, запрос падает как обычная сетевая ошибка, а пользователя НЕ
    /// разлогинивает — сессия продлится при следующем запросе (порт Android 955fd69).</summary>
    private async Task<RefreshOutcome> RefreshSessionAsync(string refreshToken, CancellationToken ct)
    {
        HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh") { Content = new StringContent("") };
            request.Headers.Add("Cookie", $"rw_refresh_token={refreshToken}");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return request;
        }

        HttpResponseMessage response;
        using (var request = Build())
        {
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (VpnEngine.Current?.IsRunning == true)
            {
                // Как и у обычных запросов: сбой на выходном узле туннеля — пробуем напрямую.
                using var direct = Build();
                response = await _directHttp.SendAsync(direct, ct).ConfigureAwait(false);
            }
        }

        using (response)
        {
            var newToken = ExtractCookieValue(response, "rw_session_token");
            if ((response.IsSuccessStatusCode || (int)response.StatusCode == 409) && newToken != null)
            {
                _tokenStore.Save(newToken);
                // Ротация refresh-токена — если бэкенд не прислал новый, оставляем прежний (мог
                // быть выдан на длительный срок и не ротируется на каждое обновление).
                if (ExtractCookieValue(response, "rw_refresh_token") is { } newRefreshToken)
                    _tokenStore.SaveRefreshToken(newRefreshToken);
                return RefreshOutcome.Refreshed;
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                return RefreshOutcome.Rejected;
            throw new HttpRequestException($"Не удалось продлить сессию: HTTP {(int)response.StatusCode}");
        }
    }

    /// <summary>Set-Cookie может встречаться несколько раз в одном ответе (rw_session_token +
    /// rw_refresh_token) — берём только нужное по имени, до первой ';' (остальное — атрибуты
    /// куки: Path/Expires/HttpOnly/Secure/SameSite, не часть значения).</summary>
    internal static string? ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            if (!cookie.StartsWith($"{cookieName}=", StringComparison.Ordinal)) continue;
            var value = cookie[(cookieName.Length + 1)..];
            var semi = value.IndexOf(';');
            return semi >= 0 ? value[..semi] : value;
        }
        return null;
    }

    private static async Task<TResponse> ReadOrThrowAsync<TResponse>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(response.StatusCode, body);
        if (typeof(TResponse) == typeof(object))
            return default!;
        return JsonSerializer.Deserialize<TResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Пустой ответ сервера");
    }

    /// <summary>Как ReadOrThrowAsync, но десериализованный null — законный результат, а не
    /// ошибка (см. комментарий у GetNullableAsync/SupportTicketsResponse: Go nil-slice на
    /// пустом списке сериализуется как буквальный "null", а не "[]").</summary>
    private static async Task<TResponse?> ReadNullableOrThrowAsync<TResponse>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ApiException(response.StatusCode, body);
        return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
    }
}

public sealed class ApiException(HttpStatusCode statusCode, string body)
    : Exception($"HTTP {(int)statusCode}: {body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
