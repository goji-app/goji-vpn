package xyz.gojihub.vpn.vpn.geo

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Раз в неделю (см. GodjiApplication.scheduleMobileWhitelistRefresh) подтягивает свежую
 *  версию hxehex/russia-mobile-internet-whitelist — см. MobileWhitelistDownloader. */
@HiltWorker
class MobileWhitelistRefreshWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        if (MobileWhitelistDownloader.refresh(applicationContext)) Result.success() else Result.retry()
    }

    companion object {
        const val UNIQUE_WORK_NAME = "mobile_whitelist_weekly_refresh"
    }
}
