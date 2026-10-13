package xyz.gojihub.vpn.settings

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import xyz.gojihub.vpn.i18n.AppLanguage
import xyz.gojihub.vpn.ui.theme.FontSizePreset
import xyz.gojihub.vpn.ui.theme.ThemeMode
import xyz.gojihub.vpn.util.LogLevel
import javax.inject.Inject
import javax.inject.Singleton

private val Context.dataStore by preferencesDataStore(name = "godji_settings")

/** Способ замера пинга серверов — см. ServersScreen/PingRepository.
 *  PROXY_GET/PROXY_HEAD — через встроенный в libXray pingBatch (реальный HTTP-запрос через
 *  временный Xray-инстанс на кандидата, учитывает задержку самого прокси-протокола, не только
 *  сетевой RTT); PROXY_HEAD передаёт HTTP-метод в тот же payload — если конкретная сборка
 *  libXray его не читает, тихо ведёт себя как GET (не ломается, просто не даёт экономии).
 *  TCP — обычный TCP-connect до host:port узла, без прокси (быстрее, но не видит реальную
 *  задержку самого VLESS/Reality-протокола). ICMP — системная утилита ping (без root; работает,
 *  потому что бинарник /system/bin/ping имеет нужный capability на большинстве прошивок). */
enum class PingMethod { PROXY_GET, PROXY_HEAD, TCP, ICMP }

/** Per-app proxy (см. AppTunnelingScreen) — та же идея, что "Прокси для выбранных приложений"
 *  у Happ. OFF: без пользовательского списка (только встроенный RU_APPS_EXCLUDED_FROM_VPN в
 *  GodjiVpnService). ALLOW: через VPN идут ТОЛЬКО выбранные пакеты, остальное — напрямую
 *  (Android VpnService.Builder.addAllowedApplication). BYPASS: выбранные пакеты идут напрямую,
 *  остальное — через VPN (addDisallowedApplication, объединяется со встроенным RU-списком). */
enum class PerAppProxyMode { OFF, ALLOW, BYPASS }

/** Порядок списка на экране "Серверы": избранные сверху (иначе — порядок подписки), по пингу
 *  (непроверенные и недоступные — в конце), по алфавиту. */
enum class ServerSort { FAVORITES, PING, NAME }

@Singleton
class SettingsRepository @Inject constructor(@ApplicationContext private val context: Context) {

    private val preferredNodeKey = stringPreferencesKey("preferred_node_id")
    private val pinNotificationKey = booleanPreferencesKey("pin_notification")
    private val pingMethodKey = stringPreferencesKey("ping_method")
    private val pingTestUrlKey = stringPreferencesKey("ping_test_url")
    private val darkThemeKey = booleanPreferencesKey("dark_theme_enabled") // legacy, см. themeMode ниже
    private val themeModeKey = stringPreferencesKey("theme_mode")
    private val appLanguageKey = stringPreferencesKey("app_language")
    private val logLevelKey = stringPreferencesKey("log_level")
    private val fontSizeKey = stringPreferencesKey("font_size_preset")
    private val autoConnectOnWifiKey = booleanPreferencesKey("auto_connect_on_wifi")
    private val killSwitchKey = booleanPreferencesKey("kill_switch_enabled")
    private val lastConnectPayloadKey = stringPreferencesKey("last_connect_payload")
    private val lastConnectLabelKey = stringPreferencesKey("last_connect_label")
    private val perAppProxyModeKey = stringPreferencesKey("per_app_proxy_mode")
    private val perAppProxyPackagesKey = stringSetPreferencesKey("per_app_proxy_packages")
    private val favoriteServerIdsKey = stringSetPreferencesKey("favorite_server_ids")
    private val hapticsKey = booleanPreferencesKey("haptics_enabled")
    private val serverSortKey = stringPreferencesKey("server_sort")
    private val trustedSsidsKey = stringSetPreferencesKey("trusted_wifi_ssids")
    private val disconnectOnTrustedKey = booleanPreferencesKey("disconnect_on_trusted_wifi")
    private val bypassDomainsKey = stringSetPreferencesKey("bypass_domains")

    val preferredNodeId: Flow<String?> = context.dataStore.data.map { it[preferredNodeKey] }

    suspend fun setPreferredNodeId(nodeId: String?) {
        context.dataStore.edit {
            if (nodeId == null) it.remove(preferredNodeKey) else it[preferredNodeKey] = nodeId
        }
    }

    /** По умолчанию true — статус-уведомление закреплено (не смахивается), как и было всегда
     *  до появления этой настройки. */
    val pinNotification: Flow<Boolean> = context.dataStore.data.map { it[pinNotificationKey] ?: true }
    suspend fun setPinNotification(pinned: Boolean) {
        context.dataStore.edit { it[pinNotificationKey] = pinned }
    }
    suspend fun pinNotificationNow(): Boolean = pinNotification.first()

    val pingMethod: Flow<PingMethod> = context.dataStore.data.map {
        runCatching { PingMethod.valueOf(it[pingMethodKey] ?: PingMethod.PROXY_GET.name) }.getOrDefault(PingMethod.PROXY_GET)
    }
    suspend fun setPingMethod(method: PingMethod) {
        context.dataStore.edit { it[pingMethodKey] = method.name }
    }
    suspend fun pingMethodNow(): PingMethod = pingMethod.first()

    val pingTestUrl: Flow<String> = context.dataStore.data.map { it[pingTestUrlKey] ?: DEFAULT_PING_URL }
    suspend fun setPingTestUrl(url: String) {
        context.dataStore.edit { it[pingTestUrlKey] = url }
    }
    suspend fun pingTestUrlNow(): String = pingTestUrl.first()

    /** Новый пользователь (или тот, кто никогда явно не трогал переключатель темы) получает
     *  SYSTEM — приложение следует теме телефона. Тому, кто раньше explicit включил старый
     *  булевый "Тёмная тема", при первом чтении после обновления мигрируем выбор в DARK, а не
     *  молча переводим на SYSTEM — явный прошлый выбор не должен потеряться. */
    val themeMode: Flow<ThemeMode> = context.dataStore.data.map { prefs ->
        prefs[themeModeKey]?.let { raw -> runCatching { ThemeMode.valueOf(raw) }.getOrNull() }
            ?: if (prefs[darkThemeKey] == true) ThemeMode.DARK else ThemeMode.SYSTEM
    }
    suspend fun setThemeMode(mode: ThemeMode) {
        context.dataStore.edit { it[themeModeKey] = mode.name }
    }
    suspend fun themeModeNow(): ThemeMode = themeMode.first()

    val appLanguage: Flow<AppLanguage> = context.dataStore.data.map { AppLanguage.fromCode(it[appLanguageKey]) }
    suspend fun setAppLanguage(language: AppLanguage) {
        context.dataStore.edit { it[appLanguageKey] = language.code }
    }
    suspend fun appLanguageNow(): AppLanguage = appLanguage.first()

    val logLevel: Flow<LogLevel> = context.dataStore.data.map { LogLevel.fromName(it[logLevelKey]) }
    suspend fun setLogLevel(level: LogLevel) {
        context.dataStore.edit { it[logLevelKey] = level.name }
    }
    suspend fun logLevelNow(): LogLevel = logLevel.first()

    val fontSize: Flow<FontSizePreset> = context.dataStore.data.map {
        runCatching { FontSizePreset.valueOf(it[fontSizeKey] ?: FontSizePreset.NORMAL.name) }.getOrDefault(FontSizePreset.NORMAL)
    }
    suspend fun setFontSize(preset: FontSizePreset) {
        context.dataStore.edit { it[fontSizeKey] = preset.name }
    }
    suspend fun fontSizeNow(): FontSizePreset = fontSize.first()

    /** По умолчанию выключено — авто-старт VPN на пользователя без явного согласия был бы
     *  неожиданным поведением, особенно на первом Wi-Fi после установки. */
    val autoConnectOnWifi: Flow<Boolean> = context.dataStore.data.map { it[autoConnectOnWifiKey] ?: false }
    suspend fun setAutoConnectOnWifi(enabled: Boolean) {
        context.dataStore.edit { it[autoConnectOnWifiKey] = enabled }
    }

    /** По умолчанию выключено — при включении кратковременные "окна" во время
     *  переключения сервера/автовосстановления после сбоя (см. GodjiVpnService.stopTunnel)
     *  вместо отката на обычную сеть блокируют весь трафик до восстановления туннеля, что для
     *  части пользователей может неожиданно означать "интернет не работает", если они не знают
     *  о причине — поэтому это осознанный выбор, а не поведение по умолчанию. */
    val killSwitchEnabled: Flow<Boolean> = context.dataStore.data.map { it[killSwitchKey] ?: false }
    suspend fun setKillSwitchEnabled(enabled: Boolean) {
        context.dataStore.edit { it[killSwitchKey] = enabled }
    }
    suspend fun killSwitchEnabledNow(): Boolean = killSwitchEnabled.first()

    /** Последний узел, на который реально отправляли ACTION_CONNECT — используется
     *  GodjiVpnService для самостоятельного подъёма туннеля, когда службу запускает сама
     *  система (Always-on VPN), а не наш собственный Intent (см. GodjiVpnService.onStartCommand). */
    suspend fun saveLastConnect(vlessPayload: String, label: String) {
        context.dataStore.edit {
            it[lastConnectPayloadKey] = vlessPayload
            it[lastConnectLabelKey] = label
        }
    }
    suspend fun lastConnectPayloadNow(): String? = context.dataStore.data.first()[lastConnectPayloadKey]
    suspend fun lastConnectLabelNow(): String? = context.dataStore.data.first()[lastConnectLabelKey]

    val perAppProxyMode: Flow<PerAppProxyMode> = context.dataStore.data.map {
        runCatching { PerAppProxyMode.valueOf(it[perAppProxyModeKey] ?: PerAppProxyMode.OFF.name) }.getOrDefault(PerAppProxyMode.OFF)
    }
    suspend fun setPerAppProxyMode(mode: PerAppProxyMode) {
        context.dataStore.edit { it[perAppProxyModeKey] = mode.name }
    }
    suspend fun perAppProxyModeNow(): PerAppProxyMode = perAppProxyMode.first()

    val perAppProxyPackages: Flow<Set<String>> = context.dataStore.data.map { it[perAppProxyPackagesKey] ?: emptySet() }
    suspend fun setPerAppProxyPackages(packages: Set<String>) {
        context.dataStore.edit { it[perAppProxyPackagesKey] = packages }
    }
    suspend fun perAppProxyPackagesNow(): Set<String> = perAppProxyPackages.first()

    /** Избранные серверы (закреплены сверху списка в ServersScreen) — просто набор id узлов,
     *  сами узлы приходят из подписки и не хранятся здесь. */
    val favoriteServerIds: Flow<Set<String>> = context.dataStore.data.map { it[favoriteServerIdsKey] ?: emptySet() }
    suspend fun toggleFavoriteServer(id: String) {
        context.dataStore.edit { prefs ->
            val current = prefs[favoriteServerIdsKey] ?: emptySet()
            prefs[favoriteServerIdsKey] = if (id in current) current - id else current + id
        }
    }

    /** Короткая вибрация при подключении/отключении VPN (см. VpnStateObserver) — по
     *  умолчанию включена. */
    val hapticsEnabled: Flow<Boolean> = context.dataStore.data.map { it[hapticsKey] ?: true }
    suspend fun setHapticsEnabled(enabled: Boolean) {
        context.dataStore.edit { it[hapticsKey] = enabled }
    }

    val serverSort: Flow<ServerSort> = context.dataStore.data.map {
        runCatching { ServerSort.valueOf(it[serverSortKey] ?: ServerSort.FAVORITES.name) }.getOrDefault(ServerSort.FAVORITES)
    }
    suspend fun setServerSort(sort: ServerSort) {
        context.dataStore.edit { it[serverSortKey] = sort.name }
    }

    /** Доверенные Wi-Fi (SSID без кавычек) — см. NetworkRulesManager: в них VPN не включается
     *  автоматически и (если включено [disconnectOnTrusted]) отключается при подключении к ним. */
    val trustedSsids: Flow<Set<String>> = context.dataStore.data.map { it[trustedSsidsKey] ?: emptySet() }
    suspend fun addTrustedSsid(ssid: String) {
        context.dataStore.edit { it[trustedSsidsKey] = (it[trustedSsidsKey] ?: emptySet()) + ssid }
    }
    suspend fun removeTrustedSsid(ssid: String) {
        context.dataStore.edit { it[trustedSsidsKey] = (it[trustedSsidsKey] ?: emptySet()) - ssid }
    }

    val disconnectOnTrusted: Flow<Boolean> = context.dataStore.data.map { it[disconnectOnTrustedKey] ?: true }
    suspend fun setDisconnectOnTrusted(enabled: Boolean) {
        context.dataStore.edit { it[disconnectOnTrustedKey] = enabled }
    }

    /** "Сайты мимо VPN": домены (уже нормализованные, в punycode), трафик к которым вместе с
     *  поддоменами GodjiVpnService отправляет напрямую — правило routing "domain -> direct". */
    val bypassDomains: Flow<Set<String>> = context.dataStore.data.map { it[bypassDomainsKey] ?: emptySet() }
    suspend fun bypassDomainsNow(): Set<String> = bypassDomains.first()
    suspend fun addBypassDomain(domain: String) {
        context.dataStore.edit { it[bypassDomainsKey] = (it[bypassDomainsKey] ?: emptySet()) + domain }
    }
    suspend fun removeBypassDomain(domain: String) {
        context.dataStore.edit { it[bypassDomainsKey] = (it[bypassDomainsKey] ?: emptySet()) - domain }
    }
    companion object {
        /** "https://www.Example.com:443/path" → "example.com"; кириллические домены → punycode.
         *  null — если это не похоже на домен. */
        fun normalizeDomain(input: String): String? {
            var d = input.trim().lowercase()
            d = d.substringAfter("://")
            d = d.substringBefore('/').substringBefore('?').substringBefore('#').substringBefore(':')
            d = d.removePrefix("*.").removePrefix(".").removePrefix("www.").trimEnd('.')
            if (d.isEmpty()) return null
            val ascii = runCatching { java.net.IDN.toASCII(d) }.getOrNull() ?: return null
            return ascii.takeIf { Regex("""^[a-z0-9-]+(\.[a-z0-9-]+)+$""").matches(it) }
        }

        const val DEFAULT_PING_URL = "https://cp.cloudflare.com/generate_204"
    }
}
