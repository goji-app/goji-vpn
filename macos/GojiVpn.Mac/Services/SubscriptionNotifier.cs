using System.IO;
using System.Text.Json;
using GodjiVpn.Models;
using GodjiVpn.Utils;

namespace GodjiVpn.Services;

/// <summary>
/// Аналог SubscriptionNotifier.kt (Android) — два локальных уведомления о подписке, оба
/// считаются на устройстве по данным, которые приложение и так уже получает при обычном
/// обновлении подписки (см. вызов из App.xaml.cs на каждый SubscriptionRepository.Subscription):
///
/// 1. Скорое окончание — за 3 дня и за 1 день для обычных тарифов, за 12 часов для триала
///    (kind:"trial"), каждое один раз на конкретный expire_at (как 3abb2a1 в Android).
/// 2. Успешная оплата — обнаруживается косвенно: оплата происходит вне приложения (сайт или
///    Telegram-бот), отдельного колбэка от бэкенда нет — сравниваем expire_at с прошлым
///    известным при каждом обновлении; если срок сдвинулся вперёд (или тариф перестал быть
///    триальным), значит оплата прошла.
/// </summary>
public sealed class SubscriptionNotifier
{
    private const double ThreeDaysHours = 72.0;
    private const double OneDayHours = 24.0;
    private const double TrialThresholdHours = 12.0;

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "sub-notify.json");

    private sealed class State
    {
        public string? LastExpireAt { get; set; }
        public string? LastKind { get; set; }
        /// <summary>Триал: предупреждение за 12 часов.</summary>
        public string? WarnedForExpireAt { get; set; }
        public string? Warned3dForExpireAt { get; set; }
        public string? Warned1dForExpireAt { get; set; }
    }

    public event Action<string, string>? NotificationRequested;

    public void Check(SubscriptionInfo sub)
    {
        var state = Load();
        var isTrial = sub.Kind == "trial";

        // lastExpireAt == null — первая проверка за процесс/установку, не считаем "оплатой",
        // иначе уведомление приходило бы при первом же входе в аккаунт.
        if (state.LastExpireAt != null &&
            DateTimeOffset.TryParse(sub.ExpireAt, out var current) &&
            DateTimeOffset.TryParse(state.LastExpireAt, out var last))
        {
            var extended = current > last;
            var trialEnded = state.LastKind == "trial" && !isTrial;
            if (extended || trialEnded)
            {
                NotificationRequested?.Invoke("Оплата прошла успешно",
                    $"Подписка «{sub.PlanName}» продлена до {DateFormat.FormatDate(sub.ExpireAt)}.");
                // Новый срок — прошлые предупреждения больше не актуальны.
                state.WarnedForExpireAt = null;
                state.Warned3dForExpireAt = null;
                state.Warned1dForExpireAt = null;
            }
        }
        state.LastExpireAt = sub.ExpireAt;
        state.LastKind = sub.Kind;
        Save(state);

        if (!DateTimeOffset.TryParse(sub.ExpireAt, out var expireAt)) return;
        var hoursLeft = (expireAt - DateTimeOffset.UtcNow).TotalHours;
        if (hoursLeft < 0) return;
        var date = DateFormat.FormatDate(sub.ExpireAt);

        if (isTrial)
        {
            if (hoursLeft <= TrialThresholdHours && state.WarnedForExpireAt != sub.ExpireAt)
            {
                NotificationRequested?.Invoke("Подписка скоро закончится",
                    "Пробный период заканчивается меньше чем через 12 часов — продлите, чтобы не потерять доступ.");
                state.WarnedForExpireAt = sub.ExpireAt;
                Save(state);
            }
            return;
        }

        // Обычный тариф — два напоминания. Если подписка впервые замечена уже в последние
        // сутки, приходит только "завтра", а "3 дня" помечается пройденным, чтобы не пришло
        // следом с устаревшим текстом.
        if (hoursLeft <= OneDayHours && state.Warned1dForExpireAt != sub.ExpireAt)
        {
            NotificationRequested?.Invoke("Подписка закончится завтра",
                $"Тариф «{sub.PlanName}» заканчивается {date}. Продли сейчас, чтобы не остаться без VPN.");
            state.Warned1dForExpireAt = sub.ExpireAt;
            state.Warned3dForExpireAt = sub.ExpireAt;
            Save(state);
        }
        else if (hoursLeft > OneDayHours && hoursLeft <= ThreeDaysHours && state.Warned3dForExpireAt != sub.ExpireAt)
        {
            NotificationRequested?.Invoke("Подписка закончится через 3 дня",
                $"Тариф «{sub.PlanName}» действует до {date}. Продли заранее — VPN не отключится в самый неудобный момент.");
            state.Warned3dForExpireAt = sub.ExpireAt;
            Save(state);
        }
    }

    private static State Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new State();
            return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
        }
        catch { return new State(); }
    }

    private static void Save(State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch { /* лучшее усилие — потеря состояния приведёт максимум к повторному уведомлению */ }
    }
}
