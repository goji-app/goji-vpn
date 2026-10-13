package xyz.gojihub.vpn.update

import xyz.gojihub.vpn.BuildConfig

/**
 * Зеркала сайта загрузки (исходники сайта — «10. Сайт загрузки», выгрузка publish.py). На каждом
 * лежат одни и те же файлы: latest.json, сборки в files/, геоданные в geo/. Пробуем по порядку,
 * GitHub — запасной путь уже у каждого вызывающего. Новое зеркало = новая строка здесь.
 */
object DownloadMirrors {
    val SITES = listOf(
        BuildConfig.DOWNLOAD_URL,
    )

    /** Абсолютные адреса файла на всех зеркалах, [path] — относительный путь вида "geo/geoip.dat". */
    fun urls(path: String): List<String> = SITES.map { "$it/${path.trimStart('/')}" }
}
