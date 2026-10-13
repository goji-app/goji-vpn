namespace GodjiVpn.Services;

/// <summary>
/// Хранение секретов на macOS (токены сессии, ссылка подписки): файл в
/// ~/Library/Application Support/GodjiVpn с правами 600 — читать его может только сам
/// пользователь. Заменяет DPAPI Windows-клиента (на macOS его нет).
/// </summary>
public static class SecretFile
{
    public static string PathFor(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", name);

    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Создаём сразу с правами 600, чтобы содержимое ни на миг не было доступно другим.
        using (var stream = new FileStream(path, new FileStreamOptions
               {
                   Mode = FileMode.Create,
                   Access = FileAccess.Write,
                   UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
               }))
        using (var writer = new StreamWriter(stream))
            writer.Write(text);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static string? Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
