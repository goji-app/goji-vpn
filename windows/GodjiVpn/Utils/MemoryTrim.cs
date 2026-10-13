using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace GodjiVpn.Utils;

/// <summary>
/// Когда окно свёрнуто или убрано в трей, приложение может провести там часы, а VPN-туннель
/// живёт в отдельном процессе sing-box. Поэтому, как только окно скрывается, собираем мусор с
/// уплотнением кучи и отдаём системе страницы рабочего набора: в «Диспетчере задач» клиент в
/// трее занимает в разы меньше памяти, а при открытии окна нужное подгружается обратно.
/// </summary>
public static class MemoryTrim
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    public static void Attach(Window window)
    {
        window.StateChanged += (_, _) => { if (window.WindowState == WindowState.Minimized) Schedule(window); };
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) Schedule(window); };
    }

    private static void Schedule(Window window)
    {
        // Чуть позже, чтобы анимация сворачивания и последние отрисовки успели пройти.
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (window.IsVisible && window.WindowState != WindowState.Minimized) return;
            Trim();
        };
        timer.Start();
    }

    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var self = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(self.Handle, -1, -1);
        }
        catch
        {
            // Чисто оптимизация — если что-то не так, просто пропускаем.
        }
    }
}
