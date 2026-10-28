package xyz.gojihub.vpn

import android.app.Application
import android.os.Build
import xyz.gojihub.vpn.update.AppUpdateChecker
import xyz.gojihub.vpn.update.AppUpdateNotifier
import xyz.gojihub.vpn.update.UpdateCheckWorker
import xyz.gojihub.vpn.subscription.PingProcessService
import java.io.File
import androidx.hilt.work.HiltWorkerFactory
import androidx.work.Configuration
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Constraints
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import coil.ImageLoader
import coil.ImageLoaderFactory
import dagger.hilt.android.HiltAndroidApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import okhttp3.OkHttpClient
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.SubscriptionRefreshWorker
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.ThemeMode
import xyz.gojihub.vpn.ui.theme.isSystemInDarkMode
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.AppVisibility
import xyz.gojihub.vpn.vpn.GeoAssets
import xyz.gojihub.vpn.vpn.GodjiVpnService
import xyz.gojihub.vpn.vpn.GojiTileService
import xyz.gojihub.vpn.vpn.LastNodeShortcut
import xyz.gojihub.vpn.vpn.VpnStateObserver
import xyz.gojihub.vpn.vpn.geo.GeoDataDownloader
import xyz.gojihub.vpn.vpn.geo.GeoDataRefreshWorker
import xyz.gojihub.vpn.vpn.geo.MobileWhitelistDownloader
import xyz.gojihub.vpn.vpn.geo.MobileWhitelistRefreshWorker
import java.util.concurrent.TimeUnit
import javax.inject.Inject

@HiltAndroidApp
class GodjiApplication : Application(), Configuration.Provider, ImageLoaderFactory {

    @Inject lateinit var workerFactory: HiltWorkerFactory
    @Inject lateinit var settingsRepository: SettingsRepository
    @Inject lateinit var subscriptionRepository: SubscriptionRepository
    @Inject lateinit var vpnStateObserver: VpnStateObserver
    @Inject lateinit var networkRulesManager: xyz.gojihub.vpn.network.NetworkRulesManager
    @Inject lateinit var pingRepository: xyz.gojihub.vpn.subscription.PingRepository
    @Inject lateinit var tokenManager: xyz.gojihub.vpn.auth.TokenManager
    @Inject lateinit var networkMonitor: xyz.gojihub.vpn.network.NetworkMonitor

    @Volatile private var lastForegroundRefreshAt = 0L

    override val workManagerConfiguration: Configuration
        get() = Configuration.Builder().setWorkerFactory(workerFactory).build()

    // Наше приложение исключено из собственного VPN-туннеля (см. addDisallowedApplication в
    // GodjiVpnService.establishTunnel) — без этого селектора картинки в новостях (RichContent)
    // грузились бы по сырой сети телефона напрямую и падали в зоне глушения мобильной сети,
    // даже когда VPN на российский узел уже подключён — тот же приём, что уже применяется к
    // собственному API-трафику приложения в NetworkModule.
    override fun newImageLoader(): ImageLoader =
        ImageLoader.Builder(this)
            .okHttpClient {
                OkHttpClient.Builder()
                    .proxySelector(GodjiVpnService.tunnelAwareProxySelector())
                    .build()
            }
            .build()

    @OptIn(kotlinx.coroutines.FlowPreview::class)
    private fun widgetPings() = pingRepository.pings.drop(1).debounce(1500)

    private fun currentProcessName(): String =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) getProcessName()
        else runCatching { File("/proc/self/cmdline").readText().trimEnd('\u0000') }.getOrDefault("")

    override fun onCreate() {
        super.onCreate()
        // Первым делом: падение в любом процессе приложения попадёт в отчёт (см. CrashReporter).
        xyz.gojihub.vpn.util.CrashReporter.install(this)
        // Заведомо раньше первого обращения к libXray где-либо в приложении — см. GeoAssets.
        GeoAssets.applyEnv(this)
        // Процесс ":ping" (PingProcessService) — только исполнитель pingBatch: ни темы, ни
        // воркеров, ни проверки обновлений там не нужно, иначе они запускались бы дважды.
        if (currentProcessName().endsWith(PingProcessService.PROCESS_SUFFIX)) return
        // Синхронно (runBlocking) и до первой отрисовки — иначе первый кадр рисуется светлой
        // темой по умолчанию, и при включённой тёмной теме экран на миг "мигает" светлым.
        // DataStore-чтение тут — попадание в уже прогретый на диске файл на несколько КБ,
        // блокировка на старте пренебрежимо мала.
        runBlocking {
            val mode = settingsRepository.themeModeNow()
            GodjiColors.themeMode = mode
            val dark = when (mode) {
                ThemeMode.DARK -> true
                ThemeMode.LIGHT -> false
                ThemeMode.SYSTEM -> isSystemInDarkMode(this@GodjiApplication)
            }
            if (dark) GodjiColors.applyDark()
            GodjiColors.fontSizePreset = settingsRepository.fontSizeNow()
            Loc.lang = settingsRepository.appLanguageNow()
            AppLogger.level = settingsRepository.logLevelNow()
        }
        AppVisibility.install()
        startNetworkJournal()
        // Виджет «Подписка» считает дни от даты окончания — сохраняем её при каждом обновлении.
        CoroutineScope(Dispatchers.Default).launch {
            subscriptionRepository.subscription.collect { sub ->
                if (sub != null) xyz.gojihub.vpn.widget.GojiSubscriptionWidgetProvider.saveExpiry(this@GodjiApplication, sub.expireAt)
            }
        }
        // Разово для входов через браузер, сделанных до 1.0.112 (см. TokenManager).
        if (tokenManager.isLoggedIn() && tokenManager.refreshToken() == null) {
            android.os.Handler(mainLooper).post { tokenManager.recoverWebRefreshToken() }
        }
        vpnStateObserver.addListener { running ->
            GojiTileService.requestUpdate(this)
            // Узел только что стал "последним" — обновляем подпись ярлыка на иконке.
            if (running) CoroutineScope(Dispatchers.IO).launch { LastNodeShortcut.publish(this@GodjiApplication) }
        }
        vpnStateObserver.start()
        networkRulesManager.start()
        // Пинги в списке серверов большого виджета — обновляем виджет, когда пришли новые.
        CoroutineScope(Dispatchers.Default).launch {
            widgetPings().collect { xyz.gojihub.vpn.widget.GojiWidgetProvider.refresh(this@GodjiApplication) }
        }
        CoroutineScope(Dispatchers.IO).launch { LastNodeShortcut.publish(this@GodjiApplication) }
        schedulePeriodicRefresh()
        scheduleGeoDataRefresh()
        scheduleMobileWhitelistRefresh()
        // Самообновление — только в сборке site (сайт загрузки / GitHub); Google Play запрещает
        // обновлять приложение в обход своего механизма, поэтому в play его нет вовсе.
        if (BuildConfig.ENABLE_SELF_UPDATE) {
            scheduleUpdateCheck()
            // Разовая проверка сразу после запуска — периодический воркер проверит в течение суток.
            CoroutineScope(Dispatchers.IO).launch {
                AppUpdateChecker.checkForUpdate()?.let { AppUpdateNotifier.notifyIfNew(this@GodjiApplication, it) }
            }
        } else {
            WorkManager.getInstance(this).cancelUniqueWork(UpdateCheckWorker.UNIQUE_WORK_NAME)
        }
        // Разовая попытка сразу после установки/первого запуска — периодический воркер и так
        // рано или поздно скачает свежие geoip.dat/geosite.dat, но при первом же реальном
        // подключении (см. GodjiVpnService.resolveGeoDataRules) лучше уже иметь российский
        // набор runetfreedom, а не только общий, встроенный в assets (см. GeoAssets).
        // Только если давно не проверяли: раньше ~26 МБ геоданных скачивались заново при КАЖДОМ
        // старте процесса (запуск приложения, перезапуск после выгрузки системой, Always-on).
        CoroutineScope(Dispatchers.IO).launch { GeoDataDownloader.refresh(this@GodjiApplication, maxAgeMs = TimeUnit.HOURS.toMillis(24)) }
        CoroutineScope(Dispatchers.IO).launch { MobileWhitelistDownloader.refresh(this@GodjiApplication, maxAgeMs = TimeUnit.DAYS.toMillis(7)) }

        // Уведомление о новой новости (см. BroadcastNotifier, вызывается изнутри
        // subscriptionRepository.refresh()) иначе появлялось бы только раз в час (фоновый
        // воркер) или при заходе именно на вкладку "Подписка" — свежий экземпляр её
        // ViewModel грузится один раз при первом создании и не перечитывается при каждом
        // возврате на уже открытую вкладку. При каждом возврате приложения на передний план
        // дополнительно дёргаем тот же refresh() — минимальный интервал 60с защищает от
        // повторных сетевых запросов при частом быстром переключении между приложениями.
        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) {
                val now = System.currentTimeMillis()
                if (now - lastForegroundRefreshAt < 60_000) return
                lastForegroundRefreshAt = now
                CoroutineScope(Dispatchers.IO).launch { subscriptionRepository.refreshIfStale() }
            }
        })
    }

    /** "Автообновление подписки каждый час, когда приложение активно или в фоне" —
     *  enqueueUniquePeriodicWork с KEEP не пересоздаёт задачу при каждом запуске процесса,
     *  так что реальный интервал остаётся ровно часовым, а не "час с момента последнего старта". */
    private fun schedulePeriodicRefresh() {
        val request = PeriodicWorkRequestBuilder<SubscriptionRefreshWorker>(1, TimeUnit.HOURS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            SubscriptionRefreshWorker.UNIQUE_WORK_NAME,
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    /** runetfreedom/russia-v2ray-rules-dat обновляется каждые 6 часов на их стороне — раз в
     *  сутки достаточно, чтобы не отставать надолго, не гоняя загрузку слишком часто впустую. */
    private fun scheduleGeoDataRefresh() {
        val request = PeriodicWorkRequestBuilder<GeoDataRefreshWorker>(24, TimeUnit.HOURS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            GeoDataRefreshWorker.UNIQUE_WORK_NAME,
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    /** hxehex/russia-mobile-internet-whitelist — вручную пополняемый краудсорс-список
     *  (см. MobileWhitelistDownloader), обновляется гораздо реже, чем category-ru — раз в
     *  неделю достаточно для актуализации адресов, не гоняя загрузку 470КБ+ CIDR-файла
     *  чаще, чем реально нужно. */
    private fun scheduleMobileWhitelistRefresh() {
        val request = PeriodicWorkRequestBuilder<MobileWhitelistRefreshWorker>(7, TimeUnit.DAYS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            MobileWhitelistRefreshWorker.UNIQUE_WORK_NAME,
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    /** Раз в сутки — новые версии выходят не так часто, чтобы проверять чаще; ручная
     *  проверка в Настройках и разовый запуск в onCreate() (см. выше) покрывают случай
     *  "хочу узнать прямо сейчас". */
    private fun scheduleUpdateCheck() {
        val request = PeriodicWorkRequestBuilder<UpdateCheckWorker>(24, TimeUnit.HOURS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            UpdateCheckWorker.UNIQUE_WORK_NAME,
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    /** Журнал сети (journal/NetworkJournal): подключения/отключения, смена сети, ошибки
     *  туннеля и «пульс» раз в 5 минут, пока VPN поднят. Автопереключения узлов пишет
     *  ConnectViewModel, правила Wi-Fi — NetworkRulesManager, проверку утечек — SettingsViewModel. */
    private fun startNetworkJournal() {
        val journal = xyz.gojihub.vpn.journal.NetworkJournal
        journal.init(this, GodjiVpnService.isRunning.value)
        val scope = CoroutineScope(kotlinx.coroutines.SupervisorJob() + Dispatchers.Default)
        scope.launch {
            var lastStable: Boolean? = null
            kotlinx.coroutines.flow.combine(GodjiVpnService.isRunning, GodjiVpnService.isConnecting) { r, c -> r to c }
                .distinctUntilChanged()
                .collect { (running, connecting) ->
                    if (connecting && !running) return@collect
                    val prev = lastStable
                    lastStable = running
                    if (prev == null || prev == running) return@collect
                    if (running) {
                        val node = subscriptionRepository.selectedNode()?.name?.let { xyz.gojihub.vpn.util.stripLeadingFlag(it) }.orEmpty()
                        journal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.CONNECTED, node)
                    } else {
                        journal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.DISCONNECTED)
                    }
                }
        }
        scope.launch {
            GodjiVpnService.isRunning.collectLatest { running ->
                while (running) {
                    journal.heartbeat()
                    kotlinx.coroutines.delay(xyz.gojihub.vpn.journal.NetworkJournal.HEARTBEAT_INTERVAL_MS)
                }
            }
        }
        scope.launch {
            networkMonitor.state.debounce(1500L).distinctUntilChanged().drop(1).collect { net ->
                val kind = when (net) {
                    xyz.gojihub.vpn.network.NetState.WIFI -> xyz.gojihub.vpn.journal.NetworkJournal.Kind.NET_WIFI
                    xyz.gojihub.vpn.network.NetState.CELLULAR -> xyz.gojihub.vpn.journal.NetworkJournal.Kind.NET_CELLULAR
                    xyz.gojihub.vpn.network.NetState.JAMMED -> xyz.gojihub.vpn.journal.NetworkJournal.Kind.NET_JAMMED
                }
                val arg = if (net == xyz.gojihub.vpn.network.NetState.WIFI) networkRulesManager.currentSsid.value.orEmpty() else ""
                journal.log(kind, arg)
            }
        }
        scope.launch {
            GodjiVpnService.lastError.collect { error ->
                if (!error.isNullOrBlank()) journal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.ERROR, error)
            }
        }
    }
}
