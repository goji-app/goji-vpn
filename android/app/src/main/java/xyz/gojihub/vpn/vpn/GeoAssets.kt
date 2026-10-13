package xyz.gojihub.vpn.vpn

import android.content.Context
import android.system.Os
import android.util.Log
import java.io.File

/**
 * geoip.dat/geosite.dat нужны Xray-core для правил маршрутизации вроде geoip:private,
 * которые реально присутствуют в конфигах подписки. Установка переменной
 * "xray.location.asset" прямо перед runXray (в GodjiVpnService) не срабатывала —
 * та же ошибка "no such file or directory" после неё, хотя имя переменной подтверждено
 * strings-разбором libgojni.so. Похоже на типичную для gomobile/cgo проблему: Go-рантайм
 * кэширует окружение при загрузке нативной библиотеки (dlopen), а libc setenv() из Kotlin
 * после этого момента Go уже не видит. Поэтому выставляем переменную здесь, в
 * Application.onCreate() — заведомо раньше первого обращения к классу LibXray где-либо
 * в приложении (единственное место, где он используется — GodjiVpnService, поднимается
 * куда позже, из foreground-сервиса по действию пользователя).
 */
object GeoAssets {
    private const val TAG = "GodjiVpn"

    fun applyEnv(context: Context) {
        val dir = ensureDir(context)
        Os.setenv("xray.location.asset", dir.absolutePath, true)
        Os.setenv("XRAY_LOCATION_ASSET", dir.absolutePath, true)
        Log.d(TAG, "GeoAssets.applyEnv: xray.location.asset=${dir.absolutePath}")
    }

    private fun ensureDir(context: Context): File {
        val dir = File(context.filesDir, "geoassets")
        if (!dir.exists()) dir.mkdirs()
        listOf(
            "geoip.dat", "geosite.dat",
            "mobile_whitelist_domains.txt", "mobile_whitelist_cidr.txt"
        ).forEach { name ->
            val dest = File(dir, name)
            if (!dest.exists()) {
                context.assets.open("geoassets/$name").use { input ->
                    dest.outputStream().use { output -> input.copyTo(output) }
                }
            }
        }
        return dir
    }
}
