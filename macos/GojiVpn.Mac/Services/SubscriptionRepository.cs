using System.ComponentModel;
using System.IO;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Держит текущую подписку и список узлов в памяти, отдаёт их и Connect-, и Servers-экрану —
/// прямой аналог SubscriptionRepository.kt (Android): один источник правды, обновляемый через
/// Refresh(), вместо того чтобы каждый экран тянул <кабинет>/сервер подписки заново.
/// </summary>
public sealed class SubscriptionRepository : INotifyPropertyChanged
{
    private readonly ApiClient _api;
    private readonly SubscriptionService _subscriptionService;
    private readonly CustomNodeStore _customNodes;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Последняя известная подписка с диска (AccountCache) — «Главная» и «Подписка» показывают её
    // сразу после запуска, не дожидаясь сети.
    private Models.SubscriptionInfo? _subscription = AccountCache.Load<Models.SubscriptionInfo>(AccountCache.Key.Subscription)?.Value;
    public Models.SubscriptionInfo? Subscription
    {
        get => _subscription;
        private set { _subscription = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subscription))); }
    }

    private IReadOnlyList<VlessNode> _subscriptionNodes = Array.Empty<VlessNode>();

    /// <summary>Узлы подписки + добавленные вручную по JSON-профилю (см. CustomNodeStore) —
    /// единый список для Connect/Servers/Plans, ничего в потребителях менять не пришлось: они
    /// как читали Nodes/SelectedNode отсюда, так и продолжают, просто набор пополнился.</summary>
    public IReadOnlyList<VlessNode> Nodes => _subscriptionNodes.Concat(_customNodes.Nodes).ToList();

    private static string SelectedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "selected-node.txt");

    /// <summary>Выбранный узел переживает перезапуск (как NodeListCache в Android) — раньше после
    /// каждого запуска выбирался первый узел подписки, а не тот, что пользователь выбрал сам.</summary>
    private string? _selectedId = LoadSelectedId();
    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            _selectedId = value;
            SaveSelectedId(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedId)));
        }
    }

    private static string? LoadSelectedId()
    {
        try
        {
            if (!File.Exists(SelectedPath)) return null;
            var id = File.ReadAllText(SelectedPath).Trim();
            // Старый формат ID — позиция в массиве ("0", "1"…) — больше ничего не значит.
            return id.Length == 0 || id.All(char.IsDigit) ? null : id;
        }
        catch { return null; }
    }

    private static void SaveSelectedId(string? id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SelectedPath)!);
            if (id == null) File.Delete(SelectedPath);
            else File.WriteAllText(SelectedPath, id);
        }
        catch { /* лучшее усилие — максимум при следующем запуске выберется первый узел */ }
    }

    /// <summary>Причина, по которой RefreshAsync() в последний раз вернул false — раньше все
    /// сбои (сеть, 401/403/500, пустой список подписок) молча проглатывались, и пользователь
    /// видел просто пустой экран без единого объяснения, почему "не подтянуло подписку".</summary>
    private string? _lastError;
    public string? LastError
    {
        get => _lastError;
        private set { _lastError = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastError))); }
    }

    /// <summary>Новости/рассылки (см. ApiClient.GetBroadcastsAsync) — та же страница, что
    /// "Мои рассылки" веб-версии. Тоже с диска (AccountCache): без этого раздел новостей на
    /// «Подписке» пустовал при каждом запуске без сети.</summary>
    private IReadOnlyList<BroadcastDto> _broadcasts =
        AccountCache.Load<List<BroadcastDto>>(AccountCache.Key.Broadcasts)?.Value ?? (IReadOnlyList<BroadcastDto>)Array.Empty<BroadcastDto>();
    public IReadOnlyList<BroadcastDto> Broadcasts
    {
        get => _broadcasts;
        private set { _broadcasts = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Broadcasts))); }
    }

    private const string TrialError = "Не удалось активировать пробный период";

    /// <summary>Пробный период, который можно включить прямо из приложения (только у аккаунта
    /// без подписки и только бесплатный): число дней (0, если бэкенд его не сообщил); null —
    /// предлагать нечего. Порт Android SubscriptionRepository.trialDays.</summary>
    private int? _trialDays;
    public int? TrialDays
    {
        get => _trialDays;
        private set { _trialDays = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrialDays))); }
    }

    // Поколение предложения: ответ api/dashboard, ушедший до активации или выхода, не должен
    // вернуть уже скрытую карточку.
    private int _trialGeneration;

    public SubscriptionRepository(ApiClient api, SubscriptionService subscriptionService, CustomNodeStore customNodes)
    {
        _api = api;
        _subscriptionService = subscriptionService;
        _customNodes = customNodes;
        _customNodes.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nodes)));
    }

    public VlessNode? SelectedNode => Nodes.FirstOrDefault(n => n.Id == SelectedId);

    // Автоматические обновления (запуск, открытие вкладок) — не чаще раза в час: часовой таймер
    // App и так обновляет подписку, а кнопки «обновить» — сразу (RefreshAsync). Одновременные
    // вызовы склеиваются в один.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTime _lastOkRefreshUtc = DateTime.MinValue;

    /// <summary>Данные прежнего аккаунта стёрты (выход/истёкшая сессия) — вкладки сбрасывают
    /// то, что показывали.</summary>
    public event Action? AccountCleared;

    /// <summary>Обновление, если с последнего удачного прошло больше часа или серверов ещё нет
    /// (список узлов на диске не хранится — после запуска его нужно получить).</summary>
    public async Task<bool> RefreshIfStaleAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (_subscriptionNodes.Count > 0 && DateTime.UtcNow - _lastOkRefreshUtc < AutoRefreshInterval) return true;
            return await RefreshUnlockedAsync();
        }
        finally { _refreshGate.Release(); }
    }

    /// <returns>true, если подписку удалось реально получить с бэкенда.</returns>
    public async Task<bool> RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try { return await RefreshUnlockedAsync(); }
        finally { _refreshGate.Release(); }
    }

    /// <summary>Выход из аккаунта: забыть подписку, новости и серверы прежнего аккаунта — и на
    /// диске, и в памяти, чтобы следующий вошедший не увидел их до первого обновления.</summary>
    public void ClearAccount()
    {
        AccountCache.Clear();
        SubscriptionPeriod.Clear();
        _lastOkRefreshUtc = DateTime.MinValue;
        _subscriptionNodes = Array.Empty<VlessNode>();
        Subscription = null;
        _trialGeneration++;
        TrialDays = null;
        Broadcasts = Array.Empty<BroadcastDto>();
        LastError = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nodes)));
        AccountCleared?.Invoke();
    }

    private async Task<bool> RefreshUnlockedAsync()
    {
        var ok = await RefreshCoreAsync();
        // Неудачную попытку не запоминаем: следующее открытие вкладки попробует снова.
        if (ok) _lastOkRefreshUtc = DateTime.UtcNow;
        return ok;
    }

    private async Task<bool> RefreshCoreAsync()
    {
        Models.SubscriptionsResponse subsResponse;
        try
        {
            subsResponse = await _api.GetSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            // кабинет (наш шоп-бэкенд, откуда обычно приходит ссылка подписки) и
            // сервер подписки (сам Remnawave, куда она указывает) — два независимых хоста на
            // разной инфраструктуре. Раньше недоступность ПЕРВОГО обрывала RefreshAsync целиком,
            // даже не пытаясь дойти до второго — список серверов переставал обновляться, хотя
            // сам Remnawave всё это время был доступен напрямую (тот же путь, что использует
            // любой сторонний v2ray-клиент, которому эту ссылку один раз вставили вручную).
            // Теперь при сбое здесь пробуем последнюю успешно закешированную ссылку и всё равно
            // пытаемся обновить хотя бы список серверов — план/трафик тогда просто останутся
            // прежними до восстановления кабинета.
            var cachedLink = SubscriptionLinkCache.Load();
            if (cachedLink == null)
            {
                LastError = "Не удалось получить подписку: " + ex.Message;
                return false;
            }

            var (cachedNodes, _) = await SafeFetchNodesAsync(cachedLink);
            if (cachedNodes.Count == 0)
            {
                LastError = "Не удалось получить подписку: " + ex.Message;
                return false;
            }

            // Серверы обновились по запасной ссылке, но тариф, срок и трафик из личного кабинета —
            // нет. Раньше это проходило молча: вкладка «Подписка» просто оставалась пустой, и
            // понять, что не так, было невозможно. Теперь причина видна.
            LastError = "Данные личного кабинета не загрузились: " + Shorten(ex.Message);
            _subscriptionNodes = cachedNodes;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nodes)));
            if (SelectedId == null || Nodes.All(n => n.Id != SelectedId))
                SelectedId = cachedNodes[0].Id;
            return true;
        }

        // Не завязано на наличие активной подписки — новости могут быть релевантны и до
        // покупки тарифа. Отдельная от подписки/серверов ошибка не должна прерывать остальной
        // RefreshAsync, поэтому просто отбрасывается.
        try
        {
            Broadcasts = await _api.GetBroadcastsAsync();
            AccountCache.Save(AccountCache.Key.Broadcasts, Broadcasts);
        }
        catch { /* новости не критичны для основного функционала */ }

        var active = subsResponse.Subscriptions.FirstOrDefault(s => s.IsPrimary)
            ?? subsResponse.Subscriptions.FirstOrDefault();

        // С бэкенда 7.1.0 traffic не приходит в списке (см. SubscriptionInfo.Traffic) — если
        // его нет, дозапрашиваем одиночным эндпоинтом. Сбой дозапроса не должен откатывать уже
        // полученную active — просто трафик останется пустым до следующего обновления.
        if (active != null && active.Traffic == null)
        {
            try { active = await _api.GetSubscriptionAsync(active.Id); }
            catch { /* не критично — попробуем на следующем RefreshAsync */ }
        }

        // Сюда доходим, только если кабинет реально ответил: при сбое запроса (catch выше) прежняя
        // подписка остаётся на экране. null — только если подписок у аккаунта действительно нет.
        Subscription = active;
        if (active != null) AccountCache.Save(AccountCache.Key.Subscription, active);
        else AccountCache.Remove(AccountCache.Key.Subscription);
        await RefreshTrialOfferAsync(hasSubscription: active != null);
        if (active == null)
        {
            LastError = subsResponse.Subscriptions.Count == 0
                ? "На аккаунте нет активной подписки — тариф ещё не оформлен"
                : "Не удалось определить активную подписку";
            return false;
        }

        SubscriptionLinkCache.Save(active.SubscriptionLink);
        var (nodes, nodesError) = await SafeFetchNodesAsync(active.SubscriptionLink);
        // Пустой список = сбой получения (см. SafeFetchNodesAsync), а не "в подписке теперь
        // ноль узлов" — не затираем прежний непустой список при временном сбое сети, иначе
        // пользователь на вкладке "Серверы" внезапно теряет весь список, хотя туннель (если
        // уже поднят) продолжает работать как ни в чём не бывало.
        if (nodes.Count > 0)
        {
            LastError = null;
            _subscriptionNodes = nodes;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nodes)));
            if (SelectedId == null || Nodes.All(n => n.Id != SelectedId))
                SelectedId = nodes[0].Id;
        }
        else
        {
            // device_limit тарифа — из того же /api/subscriptions, что мы уже получили выше,
            // добавляем к тексту ошибки HWID-гейта, чтобы не лезть отдельно на вкладку
            // "Подписка" ради одной цифры, когда ошибка именно про лимит устройств.
            var suffix = nodesError?.Contains("HWID", StringComparison.OrdinalIgnoreCase) == true
                ? $" (лимит устройств по тарифу «{active.PlanName}»: {active.DeviceLimit})"
                : "";
            LastError = (nodesError ?? "Список серверов пуст") + suffix;
        }
        return true;
    }

    public void Select(string id) => SelectedId = id;

    /// <summary>Включает пробный период. null — успех, иначе текст ошибки (сообщение бэкенда,
    /// если оно есть). После успеха подписка обновляется сама.</summary>
    public async Task<string?> ActivateTrialAsync()
    {
        try
        {
            var response = await _api.ActivateTrialAsync();
            if (response.Success == false)
                return string.IsNullOrWhiteSpace(response.Message) ? TrialError : response.Message.Trim();
        }
        catch (ApiException ex)
        {
            // Отказ («уже использован», «недоступен») — перепроверяем, предлагать ли карточку.
            if (ex.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                _ = RefreshTrialOfferAsync(hasSubscription: Subscription != null);
            return BackendMessage(ex.Body) ?? TrialError;
        }
        catch (Exception)
        {
            return TrialError;
        }
        _trialGeneration++;
        TrialDays = null;
        _ = RefreshAsync();
        return null;
    }

    private async Task RefreshTrialOfferAsync(bool hasSubscription)
    {
        var generation = _trialGeneration;
        if (hasSubscription) { TrialDays = null; return; }
        Models.DashboardResponse dashboard;
        try { dashboard = await _api.GetDashboardAsync(); }
        catch { return; } // не критично: карточка появится при следующем обновлении
        if (generation != _trialGeneration) return;
        TrialDays = dashboard.TrialAvailable == true && dashboard.TrialPaid != true ? dashboard.TrialDays ?? 0 : null;
    }

    /// <summary>Короткое сообщение бэкенда из тела ошибки ({"message": …} или {"error": …}).</summary>
    private static string? BackendMessage(string body)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            foreach (var key in new[] { "message", "error" })
            {
                if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String &&
                    value.GetString()?.Trim() is { Length: > 0 and <= 300 } text)
                    return text;
            }
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private static string Shorten(string message)
    {
        var oneLine = message.ReplaceLineEndings(" ");
        return oneLine.Length <= 160 ? oneLine : oneLine[..160] + "…";
    }

    private async Task<(List<VlessNode> Nodes, string? Error)> SafeFetchNodesAsync(string subscriptionLink)
    {
        try { return await _subscriptionService.FetchNodesAsync(subscriptionLink); }
        catch (Exception ex) { return (new List<VlessNode>(), "Не удалось получить список серверов: " + ex.Message); }
    }
}
