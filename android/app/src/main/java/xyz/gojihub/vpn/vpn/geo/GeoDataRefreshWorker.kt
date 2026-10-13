package xyz.gojihub.vpn.vpn.geo

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Раз в сутки (см. GodjiApplication.scheduleGeoDataRefresh) подтягивает свежие
 *  geoip.dat/geosite.dat от runetfreedom/russia-v2ray-rules-dat — см. GeoDataDownloader. */
@HiltWorker
class GeoDataRefreshWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        if (GeoDataDownloader.refresh(applicationContext)) Result.success() else Result.retry()
    }

    companion object {
        const val UNIQUE_WORK_NAME = "geo_data_daily_refresh"
    }
}
