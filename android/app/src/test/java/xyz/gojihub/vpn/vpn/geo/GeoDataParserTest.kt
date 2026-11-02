package xyz.gojihub.vpn.vpn.geo

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

/**
 * Потоковый разбор geoip.dat/geosite.dat (см. GeoDataParser.forEachWantedEntry) против
 * эталонных количеств, посчитанных независимо (прямой разбор protobuf тех же файлов из assets).
 */
class GeoDataParserTest {
    private val dir = File("src/main/assets/geoassets")

    @Test
    fun cidrsOfRequestedCountries() {
        val result = GeoDataParser.resolveCidrs(File(dir, "geoip.dat"), setOf("ru", "PRIVATE"))
        assertEquals(22825, result["RU"]?.size)
        assertEquals(18, result["PRIVATE"]?.size)
        assertEquals(setOf("RU", "PRIVATE"), result.keys)
        assertTrue(result.getValue("PRIVATE").contains("10.0.0.0/8"))
    }

    @Test
    fun domainsOfRequestedCategory() {
        val result = GeoDataParser.resolveDomains(File(dir, "geosite.dat"), setOf("CATEGORY-RU"))
        assertEquals(1092, result["CATEGORY-RU"]?.size)
        assertTrue(result.getValue("CATEGORY-RU").all { it.startsWith("domain:") || it.startsWith("regexp:") || it.isNotBlank() })
    }

    @Test
    fun unknownTagGivesNothing() {
        assertTrue(GeoDataParser.resolveCidrs(File(dir, "geoip.dat"), setOf("NO-SUCH-TAG")).isEmpty())
    }

    /** Проверка после закачки должна отсекать то, что реально встречается вместо .dat:
     *  HTML-страницу ошибки зеркала, gzip-контент (его магия 0x1F и даёт "wire type 7" из
     *  пользовательского лога) и оборванную посреди записи закачку — иначе битый файл
     *  вытесняет рабочий и роняет каждое подключение. */
    @Test
    fun validityCheckDetectsCorruptedFiles() {
        val tmp = Files.createTempDirectory("geodatatest").toFile()
        // entry{ country_code: "RU" } — минимальная валидная запись GeoSiteList/GeoIPList
        val valid = File(tmp, "valid.dat")
        valid.writeBytes(byteArrayOf(0x0A, 0x04, 0x0A, 0x02, 'R'.code.toByte(), 'U'.code.toByte()))
        assertTrue(GeoDataParser.isValidDat(valid))

        val html = File(tmp, "html.dat")
        html.writeBytes("<html><body>mirror error page</body></html>".toByteArray())
        assertFalse(GeoDataParser.isValidDat(html))

        val gzip = File(tmp, "gzip.dat")
        gzip.writeBytes(byteArrayOf(0x1F, 0x8B.toByte(), 0x08, 0x00, 0x00, 0x00, 0x00, 0x00))
        assertFalse(GeoDataParser.isValidDat(gzip))

        val truncated = File(tmp, "truncated.dat")
        truncated.writeBytes(byteArrayOf(0x0A, 100, 0x0A, 0x02, 'R'.code.toByte()))
        assertFalse(GeoDataParser.isValidDat(truncated))

        assertFalse(GeoDataParser.isValidDat(File(tmp, "missing.dat")))
    }
}
