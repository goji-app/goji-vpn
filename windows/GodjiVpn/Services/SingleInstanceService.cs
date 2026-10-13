using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GodjiVpn.Services;

/// <summary>
/// Windows не переиспользует уже запущенный процесс при клике по кастомной URI-схеме
/// (godjivpn://) так, как Android переиспользует Activity через deeplink — он просто
/// запускает новый GodjiVpn.exe с URL первым аргументом. Без этого сервиса пользователь
/// после OAuth-логина получил бы ВТОРОЕ окно приложения поверх уже открытого экрана логина.
/// Именованный Mutex определяет "я первый или уже есть работающий", именованный pipe
/// пересылает URL активации из второго (короткоживущего) процесса в первый.
/// </summary>
public sealed class SingleInstanceService
{
    private const string MutexName = "GodjiVpn.SingleInstance.Mutex";
    private const string PipeName = "GodjiVpn.SingleInstance.Pipe";

    private Mutex? _mutex;

    public event Action<string>? ActivationRequested;

    /// <returns>true — это первый (главный) экземпляр, продолжаем обычный запуск и поднимаем
    /// pipe-сервер для будущих активаций; false — уже есть работающий экземпляр.</returns>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) StartServer();
        return createdNew;
    }

    /// <summary>Вызывается ВТОРЫМ процессом: передаёт URL активации первому и должен быть
    /// сразу за этим завершён (Application.Shutdown) — окна у второго процесса не будет.</summary>
    public static void ForwardToRunningInstanceAndExit(string? activationArg)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(activationArg ?? "");
        }
        catch
        {
            // Основной процесс не отвечает (завис/умер между Mutex и стартом pipe-сервера) —
            // тут уже ничего не сделать, второй процесс всё равно завершается.
        }
    }

    /// <summary>Без явного PipeSecurity именованный канал создаётся с дефолтным DACL — на
    /// практике это означало, что ЛЮБОЙ другой процесс в этой же сессии (не только другой
    /// экземпляр GodjiVpn.exe) мог к нему подключиться и прислать что угодно. Сегодняшний
    /// обработчик (см. StartServer ниже) фактически игнорирует содержимое — только
    /// разворачивает окно, так что заметного вреда посторонний коннект не наносил, но раз
    /// приложение и так работает от администратора, канал стоит явно ограничить текущим
    /// пользователем — то же самое SID-based ограничение, которое уже даёт сама природа
    /// "один экземпляр на пользователя" у Mutex выше, просто не полагаемся на DACL по
    /// умолчанию, который может отличаться в зависимости от версии/политик Windows.</summary>
    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    private void StartServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, BuildPipeSecurity());
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(line)) ActivationRequested?.Invoke(line);
                }
                catch
                {
                    // Один неудачный цикл (обрыв соединения и т.п.) не должен останавливать
                    // сервер — пробуем слушать заново.
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        });
    }
}
