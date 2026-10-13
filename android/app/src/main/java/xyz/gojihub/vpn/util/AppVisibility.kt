package xyz.gojihub.vpn.util

import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Видно ли сейчас хоть одно окно приложения (ProcessLifecycleOwner: STARTED ↔ STOPPED).
 * ViewModel'и живут, пока не уничтожена активити, — и в свёрнутом приложении тоже, поэтому
 * их периодические опросы (скорость/таймер на Главной и т.п.) сверяются с этим флагом и не
 * будят процессор, пока пользователь ничего не видит.
 */
object AppVisibility {
    private val _visible = MutableStateFlow(false)
    val visible: StateFlow<Boolean> = _visible.asStateFlow()

    /** Вызывается один раз из GodjiApplication.onCreate (основной процесс). */
    fun install() {
        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) { _visible.value = true }
            override fun onStop(owner: LifecycleOwner) { _visible.value = false }
        })
    }
}
