using System.Runtime;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GodjiVpn.Utils;

/// <summary>
/// Когда окно свёрнуто или спрятано в строку меню, приложение может провести там часы, а
/// VPN-туннель живёт в отдельном процессе sing-box. Поэтому, как только окно скрывается,
/// собираем мусор с уплотнением кучи: освобождённую память .NET возвращает системе, и клиент
/// в фоне занимает заметно меньше.
/// </summary>
public static class MemoryTrim
{
    public static void Attach(Window window)
    {
        window.PropertyChanged += (_, e) =>
        {
            if ((e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized) ||
                (e.Property == Avalonia.Visual.IsVisibleProperty && !window.IsVisible))
                Schedule(window);
        };
    }

    private static void Schedule(Window window) =>
        // Чуть позже, чтобы анимация сворачивания и последние отрисовки успели пройти.
        DispatcherTimer.RunOnce(() =>
        {
            if (window.IsVisible && window.WindowState != WindowState.Minimized) return;
            Trim();
        }, TimeSpan.FromSeconds(2), DispatcherPriority.Background);

    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        catch
        {
            // Чисто оптимизация — если что-то не так, просто пропускаем.
        }
    }
}
