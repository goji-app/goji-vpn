package xyz.gojihub.vpn.subscription

import android.app.Service
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.Message
import android.os.Messenger
import libXray.LibXray
import org.json.JSONObject
import java.util.concurrent.Executors

/**
 * Исполнитель LibXray.invoke() в отдельном процессе ":ping" (см. AndroidManifest.xml).
 *
 * libXray отказывается выполнять pingBatch в процессе, где уже запущено "управляемое" ядро:
 * "pingBatch requires an isolated process without a managed Xray instance". А VPN-туннель
 * (GodjiVpnService) работает в основном процессе приложения — поэтому при подключённом VPN
 * все узлы на экране "Серверы" показывали "недоступен". Здесь своя копия libXray без
 * запущенного ядра. Трафик этого процесса идёт мимо туннеля так же, как и у основного:
 * addDisallowedApplication(packageName) действует на весь UID приложения, а не на процесс.
 *
 * Протокол — Messenger: запрос MSG_INVOKE с JSON-строкой запроса LibXray в KEY_REQUEST,
 * ответ MSG_RESULT с тем же arg1 (id запроса) и JSON-строкой ответа в KEY_RESPONSE.
 */
class PingProcessService : Service() {

    // LibXray держит глобальное состояние на стороне Go — вызовы строго по одному.
    private val executor = Executors.newSingleThreadExecutor()

    private val messenger = Messenger(Handler(Looper.getMainLooper()) { msg ->
        if (msg.what != MSG_INVOKE) return@Handler false
        val request = msg.data?.getString(KEY_REQUEST).orEmpty()
        val replyTo = msg.replyTo
        val id = msg.arg1
        executor.execute {
            val response = runCatching { LibXray.invoke(request) }.getOrElse {
                JSONObject().put("success", false).put("error", it.toString()).toString()
            }
            val reply = Message.obtain(null, MSG_RESULT, id, 0).apply {
                data = Bundle().apply { putString(KEY_RESPONSE, response) }
            }
            runCatching { replyTo?.send(reply) }
        }
        true
    })

    override fun onBind(intent: Intent?): IBinder = messenger.binder

    override fun onDestroy() {
        executor.shutdownNow()
        super.onDestroy()
    }

    companion object {
        const val MSG_INVOKE = 1
        const val MSG_RESULT = 2
        const val KEY_REQUEST = "request"
        const val KEY_RESPONSE = "response"
        const val PROCESS_SUFFIX = ":ping"
    }
}
