package xyz.gojihub.vpn.vpn

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.settings.SettingsRepository
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Единая точка реакции на смену состояния туннеля — независимо от того, откуда пришла
 * команда (кнопка на Главной, плитка в шторке, виджет, ярлык, Always-on, авто-переподключение).
 * Запускается один раз из GodjiApplication в основном процессе — том же, где живёт
 * GodjiVpnService и его StateFlow'ы.
 *
 * "Устойчивые" состояния считаются по паре (isRunning, isConnecting): переключение узла на
 * активном соединении проходит через (false, true) — это не отключение, вибрации нет; а вот
 * (false, false) после работавшего туннеля — настоящее отключение или обрыв.
 */
@Singleton
class VpnStateObserver @Inject constructor(
    @ApplicationContext private val context: Context,
    private val settingsRepository: SettingsRepository
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    @Volatile private var hapticsEnabled = true
    private var started = false

    /** Реакции помимо вибрации (плитка, ярлыки) — подписываются через [addListener]. */
    private val listeners = mutableListOf<(running: Boolean) -> Unit>()

    fun addListener(listener: (running: Boolean) -> Unit) {
        synchronized(listeners) { listeners += listener }
    }

    fun start() {
        if (started) return
        started = true
        scope.launch { settingsRepository.hapticsEnabled.collect { hapticsEnabled = it } }
        scope.launch {
            var lastStable: Boolean? = null
            combine(GodjiVpnService.isRunning, GodjiVpnService.isConnecting) { running, connecting -> running to connecting }
                .distinctUntilChanged()
                .collect { (running, connecting) ->
                    synchronized(listeners) { listeners.toList() }.forEach { runCatching { it(running) } }
                    // Промежуточное состояние (идёт подключение/переключение) — ждём итога.
                    if (connecting && !running) return@collect
                    val previous = lastStable
                    lastStable = running
                    // Первое значение — просто исходное состояние при старте процесса.
                    if (previous == null || previous == running) return@collect
                    if (hapticsEnabled) vibrate(connected = running)
                }
        }
    }

    private fun vibrate(connected: Boolean) {
        val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
        } ?: return
        if (!vibrator.hasVibrator()) return
        runCatching {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
                @Suppress("DEPRECATION")
                if (connected) vibrator.vibrate(longArrayOf(0, 28, 70, 28), -1) else vibrator.vibrate(40)
                return@runCatching
            }
            // Подключено — двойной короткий отклик, отключено — один короткий.
            val effect = if (connected) {
                VibrationEffect.createWaveform(longArrayOf(0, 28, 70, 28), intArrayOf(0, 200, 0, 255), -1)
            } else {
                VibrationEffect.createOneShot(40, 180)
            }
            vibrator.vibrate(effect)
        }
    }
}
