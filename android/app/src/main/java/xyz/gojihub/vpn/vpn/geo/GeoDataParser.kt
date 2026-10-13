package xyz.gojihub.vpn.vpn.geo

import java.io.BufferedInputStream
import java.io.DataInputStream
import java.io.File
import java.io.FileInputStream
import java.net.InetAddress

/**
 * Ручной protobuf-декодер под конкретную, стабильную wire-схему v2ray/xray geoip.dat/
 * geosite.dat (GeoIPList/GeoSiteList из app/router/config.proto — тот же формат, что
 * использует официальный geosite.dat, Loyalsoldier/v2ray-rules-dat и runetfreedom/
 * russia-v2ray-rules-dat). Полноценная protobuf-библиотека сюда не подключается ради
 * веса APK — схема достаточно простая и стабильная, чтобы разобрать её вручную.
 *
 * Используется вместо встроенного в Xray-core загрузчика geoip.dat/geosite.dat: тот
 * ищет файлы по переменной окружения xray.location.asset, которая в этой сборке libXray
 * до Go-рантайма не доходит (см. GeoAssets.kt) — поэтому геоданные читает и подставляет
 * в конфиг в виде обычных доменов/подсетей сам GodjiVpnService.resolveGeoDataRules().
 *
 * Не материализует доменные/IP-списки категорий, которые не были запрошены — некоторые
 * категории (например geosite:ru-blocked-all) содержат сотни тысяч записей.
 */
object GeoDataParser {

    // Полный разбор geosite.dat/geoip.dat (десятки МБ, линейный проход по всему файлу) —
    // establishTunnel() зовёт resolveDomains/resolveCidrs на КАЖДОЕ подключение: первое,
    // любое переключение узла на активном соединении, каждую попытку из RECONNECT_ATTEMPTS
    // (см. GodjiVpnService.performConnect) и каждое авто-восстановление watchdog'ом — то есть
    // потенциально по несколько раз подряд за секунды, почти всегда с ОДНИМ И ТЕМ ЖЕ набором
    // тегов (профиль узла не меняет свой routing между попытками). Раньше файл перечитывался
    // и разбирался заново каждый раз — заметный лишний вклад именно в "долгое подключение",
    // особенно на повторных попытках. Кэш на последний результат (не растущий, не на все
    // виденные наборы тегов — так проще и не грозит утечкой памяти при частой смене узлов)
    // ключуется по (путь, mtime, размер, набор тегов) — при следующей же попытке с тем же
    // профилем отдаёт готовый результат без повторного чтения файла; если файл обновился
    // (см. GeoDataDownloader) или набор тегов другой (другой узел), ключ не совпадёт и разбор
    // произойдёт заново, как раньше.
    private data class CacheKey(val path: String, val lastModified: Long, val length: Long, val tags: Set<String>)
    @Volatile private var domainCache: Pair<CacheKey, Map<String, List<String>>>? = null
    @Volatile private var cidrCache: Pair<CacheKey, Map<String, List<String>>>? = null

    fun resolveDomains(file: File, tags: Set<String>): Map<String, List<String>> {
        if (tags.isEmpty() || !file.exists()) return emptyMap()
        val wanted = tags.map { it.uppercase() }.toSet()
        val key = CacheKey(file.absolutePath, file.lastModified(), file.length(), wanted)
        domainCache?.let { (cachedKey, cachedValue) -> if (cachedKey == key) return cachedValue }
        val result = mutableMapOf<String, MutableList<String>>()
        forEachWantedEntry(file, wanted) { code, entry ->
            result.getOrPut(code) { mutableListOf() }.addAll(decodeDomains(entry, 0, entry.size))
        }
        domainCache = key to result
        return result
    }

    fun resolveCidrs(file: File, tags: Set<String>): Map<String, List<String>> {
        if (tags.isEmpty() || !file.exists()) return emptyMap()
        val wanted = tags.map { it.uppercase() }.toSet()
        val key = CacheKey(file.absolutePath, file.lastModified(), file.length(), wanted)
        cidrCache?.let { (cachedKey, cachedValue) -> if (cachedKey == key) return cachedValue }
        val result = mutableMapOf<String, MutableList<String>>()
        forEachWantedEntry(file, wanted) { code, entry ->
            result.getOrPut(code) { mutableListOf() }.addAll(decodeCidrs(entry, 0, entry.size))
        }
        cidrCache = key to result
        return result
    }

    /**
     * Потоковый проход по верхнему уровню GeoIPList/GeoSiteList (repeated entry = 1) без чтения
     * файла целиком: geoip.dat весит ~23 МБ, и раньше readBytes() на каждое (первое после
     * запуска) подключение выделял весь файл в памяти ради одной-двух нужных категорий. Теперь
     * у каждой записи читается только начало (там поле №1 — country_code), нужные записи
     * дочитываются целиком, остальные пропускаются skip'ом — пик памяти = самая большая
     * нужная категория.
     */
    private inline fun forEachWantedEntry(file: File, wanted: Set<String>, onEntry: (code: String, entry: ByteArray) -> Unit) {
        DataInputStream(BufferedInputStream(FileInputStream(file), 64 * 1024)).use { input ->
            while (true) {
                val tag = readStreamVarint(input) ?: break
                val field = (tag shr 3).toInt()
                val wireType = (tag and 0x7).toInt()
                if (field != 1 || wireType != 2) {
                    skipStream(input, wireType)
                    continue
                }
                val len = readStreamVarint(input)?.toInt() ?: break
                val prefixLen = minOf(len, HEADER_PEEK_BYTES)
                val prefix = ByteArray(prefixLen).also { input.readFully(it) }
                // country_code почти всегда первое поле записи и умещается в начало; если вдруг
                // нет — дочитываем запись целиком и ищем поле там.
                var entry: ByteArray? = null
                val code = (runCatching { peekCountryCode(prefix, 0, prefixLen) }.getOrNull()
                    ?: readRest(input, prefix, len).also { entry = it }.let { peekCountryCode(it, 0, it.size) })
                    ?.uppercase()
                if (code != null && code in wanted) {
                    onEntry(code, entry ?: readRest(input, prefix, len))
                } else if (entry == null) {
                    skipFully(input, (len - prefixLen).toLong())
                }
            }
        }
    }

    private fun readRest(input: DataInputStream, prefix: ByteArray, len: Int): ByteArray {
        val full = prefix.copyOf(len)
        if (len > prefix.size) input.readFully(full, prefix.size, len - prefix.size)
        return full
    }

    private fun readStreamVarint(input: DataInputStream): Long? {
        var result = 0L
        var shift = 0
        while (true) {
            val b = input.read()
            if (b < 0) return if (shift == 0) null else error("обрезанный varint")
            result = result or ((b.toLong() and 0x7F) shl shift)
            if (b and 0x80 == 0) return result
            shift += 7
        }
    }

    private fun skipStream(input: DataInputStream, wireType: Int) {
        when (wireType) {
            0 -> readStreamVarint(input)
            2 -> skipFully(input, readStreamVarint(input) ?: 0)
            1 -> skipFully(input, 8)
            5 -> skipFully(input, 4)
            else -> error("unsupported protobuf wire type $wireType")
        }
    }

    private fun skipFully(input: DataInputStream, count: Long) {
        var left = count
        while (left > 0) {
            val skipped = input.skip(left)
            if (skipped <= 0) {
                if (input.read() < 0) return
                left--
            } else {
                left -= skipped
            }
        }
    }

    private const val HEADER_PEEK_BYTES = 128

    /** Ищет только строковое поле №1 (country_code) в подсообщении GeoSite/GeoIP, не
     *  трогая остальные (потенциально огромные) поля — без этого пришлось бы декодировать
     *  доменные/IP-списки вообще всех категорий файла, а не только запрошенных. */
    private fun peekCountryCode(buf: ByteArray, start: Int, end: Int): String? {
        val reader = ProtoReader(buf, start, end)
        while (reader.hasMore()) {
            val (field, wireType) = reader.readTag()
            if (field == 1 && wireType == 2) {
                val (s, e) = reader.readLengthDelimitedRange()
                return String(buf, s, e - s, Charsets.UTF_8)
            } else {
                reader.skip(wireType)
            }
        }
        return null
    }

    private fun decodeDomains(buf: ByteArray, start: Int, end: Int): List<String> {
        val out = mutableListOf<String>()
        val reader = ProtoReader(buf, start, end)
        while (reader.hasMore()) {
            val (field, wireType) = reader.readTag()
            if (field == 2 && wireType == 2) {
                val (s, e) = reader.readLengthDelimitedRange()
                decodeDomain(buf, s, e)?.let { out.add(it) }
            } else {
                reader.skip(wireType)
            }
        }
        return out
    }

    /** Domain{ type: enum(1), value: string(2), attribute: repeated(3, игнорируем — только
     *  флаги вроде "только для China Ads", в наших правилах не нужны) }. Префиксы значений —
     *  ровно тот формат, что сам Xray принимает в routing.rules[].domain (см. Xray-core conf:
     *  без префикса — подстрока, "domain:" — поддомен, "full:" — точное совпадение,
     *  "regexp:" — регулярное выражение). */
    private fun decodeDomain(buf: ByteArray, start: Int, end: Int): String? {
        val reader = ProtoReader(buf, start, end)
        var type = 0L
        var value: String? = null
        while (reader.hasMore()) {
            val (field, wireType) = reader.readTag()
            when {
                field == 1 && wireType == 0 -> type = reader.readVarint()
                field == 2 && wireType == 2 -> {
                    val (s, e) = reader.readLengthDelimitedRange()
                    value = String(buf, s, e - s, Charsets.UTF_8)
                }
                else -> reader.skip(wireType)
            }
        }
        val v = value ?: return null
        return when (type.toInt()) {
            1 -> "regexp:$v"
            // 2 (Domain/корень+поддомены) и 3 (Full/точное совпадение) — здесь намеренно
            // объединены. Мы используем эти списки не для точечной блокировки рекламы, а для
            // массовой маршрутизации "весь российский сервис -> direct": если у записи стоит
            // Full (например vk.com), точное совпадение НЕ матчит реальный трафик сервиса,
            // который почти всегда идёт через поддомены API/CDN/статики (r.vk.com,
            // stats.avito.ru, socket.avito.ru). Подтверждено живым логом Xray
            // (xray_error.log через dumpXrayLogs()): именно такие запросы не матчились ни
            // одним доменным правилом и уходили в зарубежный outbound вместо direct.
            2, 3 -> "domain:$v"
            else -> v
        }
    }

    private fun decodeCidrs(buf: ByteArray, start: Int, end: Int): List<String> {
        val out = mutableListOf<String>()
        val reader = ProtoReader(buf, start, end)
        while (reader.hasMore()) {
            val (field, wireType) = reader.readTag()
            if (field == 2 && wireType == 2) {
                val (s, e) = reader.readLengthDelimitedRange()
                decodeCidr(buf, s, e)?.let { out.add(it) }
            } else {
                reader.skip(wireType)
            }
        }
        return out
    }

    /** CIDR{ ip: bytes(1) — 4 байта (IPv4) или 16 байт (IPv6), prefix: uint32(2) }. */
    private fun decodeCidr(buf: ByteArray, start: Int, end: Int): String? {
        val reader = ProtoReader(buf, start, end)
        var ip: ByteArray? = null
        var prefix = 0L
        while (reader.hasMore()) {
            val (field, wireType) = reader.readTag()
            when {
                field == 1 && wireType == 2 -> {
                    val (s, e) = reader.readLengthDelimitedRange()
                    ip = buf.copyOfRange(s, e)
                }
                field == 2 && wireType == 0 -> prefix = reader.readVarint()
                else -> reader.skip(wireType)
            }
        }
        val addr = ip ?: return null
        return "${InetAddress.getByAddress(addr).hostAddress}/$prefix"
    }

    /** Курсор поверх среза общего буфера файла, без копирования каждого подсообщения —
     *  geosite.dat весит десятки МБ (одна категория "-all" — под 700 тыс. доменов), лишние
     *  копии по каждой непрошенной категории были бы заметны по памяти/времени на телефоне. */
    private class ProtoReader(private val buf: ByteArray, start: Int, private val end: Int) {
        var pos = start

        fun hasMore() = pos < end

        fun readVarint(): Long {
            var result = 0L
            var shift = 0
            while (true) {
                val b = buf[pos++].toLong() and 0xFF
                result = result or ((b and 0x7F) shl shift)
                if (b and 0x80 == 0L) break
                shift += 7
            }
            return result
        }

        fun readTag(): Pair<Int, Int> {
            val tag = readVarint()
            return Pair((tag shr 3).toInt(), (tag and 0x7).toInt())
        }

        fun readLengthDelimitedRange(): Pair<Int, Int> {
            val len = readVarint().toInt()
            val s = pos
            pos += len
            return Pair(s, pos)
        }

        fun skip(wireType: Int) {
            when (wireType) {
                0 -> readVarint()
                2 -> { val len = readVarint().toInt(); pos += len }
                1 -> pos += 8
                5 -> pos += 4
                else -> error("unsupported protobuf wire type $wireType")
            }
        }
    }
}
