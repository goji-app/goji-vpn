using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GodjiVpn.ViewModels;

public sealed record OnboardingPage(string Title, IReadOnlyList<string> Points);

/// <summary>Первый запуск: три коротких экрана до входа (как Android и iOS, 2026-10-28) — что такое
/// Goji, как начать и какое разрешение попросит система. Показывается один раз.</summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private static string SeenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "onboarding-seen");

    public static bool IsSeen => File.Exists(SeenPath);

    public IReadOnlyList<OnboardingPage> Pages { get; } = new[]
    {
        new OnboardingPage("Работает, когда другие — нет", new[]
        {
            "Подключение в одно нажатие",
            "Российские сайты и банки — напрямую, мимо VPN",
            OperatingSystem.IsMacOS() ? "Значок в строке меню показывает, защищён ли Mac" : "Значок в трее показывает, защищён ли компьютер"
        }),
        new OnboardingPage("Как начать", new[]
        {
            "Войди по почте — придёт код, аккаунт создастся сам",
            "Включи пробный период на главном экране",
            "Нажми большую кнопку — и ты в сети"
        }),
        OperatingSystem.IsMacOS()
            ? new OnboardingPage("Одно разрешение", new[]
            {
                "При первом подключении macOS попросит пароль от Mac",
                "Он нужен, чтобы поднять защищённый туннель — Goji его не сохраняет",
                "Goji не ведёт журнал сайтов, которые ты открываешь"
            })
            : new OnboardingPage("Права администратора", new[]
            {
                "Goji запускается с правами администратора",
                "Без них Windows не даёт создать сетевой адаптер для VPN",
                "Goji не ведёт журнал сайтов, которые ты открываешь"
            })
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(IsLast), nameof(NextLabel), nameof(Dots))]
    private int index;

    public OnboardingPage Current => Pages[Index];
    public bool IsLast => Index == Pages.Count - 1;
    public string NextLabel => IsLast ? "Начать" : "Далее";
    /// <summary>Точки страниц: true — текущая.</summary>
    public IReadOnlyList<bool> Dots => Pages.Select((_, i) => i == Index).ToList();

    public event Action? Done;

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finish();
        else Index++;
    }

    [RelayCommand]
    private void Skip() => Finish();

    private void Finish()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SeenPath)!);
            File.WriteAllText(SeenPath, DateTime.UtcNow.ToString("O"));
        }
        catch { /* не записалось — покажем ещё раз при следующем запуске, не страшно */ }
        Done?.Invoke();
    }
}
