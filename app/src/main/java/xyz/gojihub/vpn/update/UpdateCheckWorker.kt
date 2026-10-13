package xyz.gojihub.vpn.update

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

/** Раз в сутки (см. GodjiApplication.scheduleUpdateCheck) проверяет, вышла ли новая версия
 *  приложения на GitHub, и уведомляет, если да (см. AppUpdateChecker/AppUpdateNotifier). */
@HiltWorker
class UpdateCheckWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = runCatching {
        AppUpdateChecker.checkForUpdate()?.let { AppUpdateNotifier.notifyIfNew(applicationContext, it) }
        Result.success()
    }.getOrElse { Result.retry() }

    companion object {
        const val UNIQUE_WORK_NAME = "app_update_daily_check"
    }
}
