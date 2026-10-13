package xyz.gojihub.vpn.subscription

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

/**
 * Раз в час (см. GodjiApplication.schedulePeriodicRefresh) обновляет подписку (список узлов,
 * уведомления об окончании/оплате/новостях) — работает и когда приложение свёрнуто.
 * Пинг серверов отсюда убран: он нужен только на экране и сам проверяется при открытии
 * приложения/вкладки "Серверы", а в фоне раз в час поднимал временные экземпляры Xray на все
 * узлы (при включённом VPN — ещё и отдельный процесс ":ping") ради цифр, которых никто не видит.
 */
@HiltWorker
class SubscriptionRefreshWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val subscriptionRepository: SubscriptionRepository
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = runCatching {
        subscriptionRepository.refresh()
        Result.success()
    }.getOrElse { Result.retry() }

    companion object {
        const val UNIQUE_WORK_NAME = "subscription_hourly_refresh"
    }
}
