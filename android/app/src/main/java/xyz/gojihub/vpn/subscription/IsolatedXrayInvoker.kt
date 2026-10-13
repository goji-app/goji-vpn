package xyz.gojihub.vpn.subscription

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.Message
import android.os.Messenger
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeout
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Клиент PingProcessService: отправляет запрос LibXray в процесс ":ping" и ждёт ответ.
 * Привязка создаётся при первом запросе и снимается через IDLE_UNBIND_MS без запросов —
 * процесс ":ping" с загруженным libXray весит ~100+ МБ, и раньше он держался всё время
 * жизни приложения ради проверки пинга раз в несколько минут. Следующий запрос привяжется
 * заново (BIND_AUTO_CREATE поднимет процесс).
 */
@Singleton
class IsolatedXrayInvoker @Inject constructor(
    @ApplicationContext private val context: Context
) {
    private val mainHandler = Handler(Looper.getMainLooper())
    private val nextId = AtomicInteger(1)
    private val pending = ConcurrentHashMap<Int, CompletableDeferred<String>>()

    @Volatile private var service: Messenger? = null
    @Volatile private var connected = CompletableDeferred<Messenger>()
    @Volatile private var binding = false
    private val idleUnbind = Runnable { unbindIfIdle() }

    private val replyMessenger = Messenger(Handler(Looper.getMainLooper()) { msg ->
        if (msg.what != PingProcessService.MSG_RESULT) return@Handler false
        val response = msg.data?.getString(PingProcessService.KEY_RESPONSE).orEmpty()
        pending.remove(msg.arg1)?.complete(response)
        true
    })

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val messenger = Messenger(binder)
            service = messenger
            connected.complete(messenger)
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            // Процесс ":ping" упал/убит — всё, что ждало ответа, завершаем ошибкой, а
            // следующий запрос дождётся новой привязки (BIND_AUTO_CREATE поднимет процесс).
            service = null
            connected = CompletableDeferred()
            failPending("ping process disconnected")
        }

        override fun onBindingDied(name: ComponentName?) {
            service = null
            connected = CompletableDeferred()
            binding = false
            failPending("ping process binding died")
            runCatching { context.unbindService(this) }
        }
    }

    /** JSON-строка запроса LibXray → JSON-строка ответа (как у LibXray.invoke). */
    suspend fun invoke(request: String, timeoutMs: Long): String {
        mainHandler.removeCallbacks(idleUnbind)
        val messenger = withTimeout(BIND_TIMEOUT_MS) { ensureBound().await() }
        val id = nextId.getAndIncrement()
        val deferred = CompletableDeferred<String>()
        pending[id] = deferred
        try {
            val msg = Message.obtain(null, PingProcessService.MSG_INVOKE, id, 0).apply {
                data = Bundle().apply { putString(PingProcessService.KEY_REQUEST, request) }
                replyTo = replyMessenger
            }
            messenger.send(msg)
            return withTimeout(timeoutMs) { deferred.await() }
        } finally {
            pending.remove(id)
            mainHandler.removeCallbacks(idleUnbind)
            mainHandler.postDelayed(idleUnbind, IDLE_UNBIND_MS)
        }
    }

    private fun unbindIfIdle() {
        if (pending.isNotEmpty() || !binding) return
        runCatching { context.unbindService(connection) }
        binding = false
        service = null
        connected = CompletableDeferred()
    }

    private fun ensureBound(): CompletableDeferred<Messenger> {
        service?.let { return CompletableDeferred(it) }
        val waiter = connected
        if (!binding) {
            binding = true
            mainHandler.post {
                val ok = runCatching {
                    context.bindService(Intent(context, PingProcessService::class.java), connection, Context.BIND_AUTO_CREATE)
                }.getOrDefault(false)
                if (!ok) {
                    binding = false
                    waiter.completeExceptionally(IllegalStateException("bindService(PingProcessService) failed"))
                    connected = CompletableDeferred()
                }
            }
        }
        return waiter
    }

    private fun failPending(reason: String) {
        val error = """{"success":false,"data":null,"error":"$reason"}"""
        pending.values.forEach { it.complete(error) }
        pending.clear()
    }

    private companion object {
        const val BIND_TIMEOUT_MS = 5_000L
        const val IDLE_UNBIND_MS = 60_000L
    }
}
