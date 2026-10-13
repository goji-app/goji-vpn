package xyz.gojihub.vpn.vpn.geo

import java.io.File

/**
 * hxehex/russia-mobile-internet-whitelist — краудсорс-список доменов и IP-подсетей,
 * остающихся доступными в РФ при вайтлистинге мобильного интернета (см.
 * MobileWhitelistDownloader). Отдельный источник от runetfreedom/russia-v2ray-rules-dat
 * (см. GeoDataParser) — обычный построчный текст, а не protobuf, парсить нечего.
 * Пустые строки и "#"-комментарии (в исходных файлах их нет, но на будущее) пропускаются.
 */
object MobileWhitelist {
    private const val DOMAINS_FILE = "mobile_whitelist_domains.txt"
    private const val CIDR_FILE = "mobile_whitelist_cidr.txt"

    /** "domain:"-префикс — тот же формат, что и GeoDataParser.decodeDomain для типов
     *  Domain/Full: матчит домен и все его поддомены, а не только точную строку из файла. */
    fun loadDomains(geoDir: File): List<String> =
        loadLines(File(geoDir, DOMAINS_FILE)).map { "domain:$it" }

    fun loadCidrs(geoDir: File): List<String> =
        loadLines(File(geoDir, CIDR_FILE))

    private fun loadLines(file: File): List<String> {
        if (!file.exists()) return emptyList()
        return file.readLines()
            .map { it.trim() }
            .filter { it.isNotEmpty() && !it.startsWith("#") }
    }
}
