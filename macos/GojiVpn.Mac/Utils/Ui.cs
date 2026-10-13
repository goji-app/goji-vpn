using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace GodjiVpn.Utils;

/// <summary>
/// Мелкие UI-операции, которые в Windows-клиенте делались напрямую через WPF (Dispatcher,
/// Clipboard, ShowDialog, OpenFileDialog) — здесь через Avalonia, в одном месте.
/// </summary>
public static class Ui
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = MainWindow is { } w ? TopLevel.GetTopLevel(w)?.Clipboard : null;
        _ = clipboard?.SetTextAsync(text);
    }

    /// <summary>Модальный диалог поверх главного окна.</summary>
    public static Task ShowDialogAsync(Window dialog) =>
        MainWindow is { } owner ? dialog.ShowDialog(owner) : ShowStandaloneAsync(dialog);

    public static Task<T?> ShowDialogAsync<T>(Window dialog) =>
        MainWindow is { } owner ? dialog.ShowDialog<T?>(owner) : Task.FromResult<T?>(default);

    private static Task ShowStandaloneAsync(Window dialog)
    {
        var tcs = new TaskCompletionSource();
        dialog.Closed += (_, _) => tcs.TrySetResult();
        dialog.Show();
        return tcs.Task;
    }

    /// <summary>Выбор файлов (фото/видео/PDF для вложений поддержки). Пустой список — отмена.</summary>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(string title, string typeName, params string[] patterns)
    {
        var owner = MainWindow;
        if (owner == null) return Array.Empty<string>();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } }
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }
}
