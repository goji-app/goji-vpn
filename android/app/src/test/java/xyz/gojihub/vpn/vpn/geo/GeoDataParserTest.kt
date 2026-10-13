package xyz.gojihub.vpn.vpn.geo

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

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
}
