package xyz.gojihub.vpn.subscription

import android.content.Context
import androidx.security.crypto.EncryptedFile
import androidx.security.crypto.MasterKey
import org.json.JSONArray
import org.json.JSONObject
import xyz.gojihub.vpn.geo.CountryGeo
import java.io.File

/**
 * Последний успешно полученный список серверов подписки (и выбранный узел), сохранённый на
 * диск — тот же приём, что в Happ/Incy: если при следующем запуске сайт (gojihub.xyz) или
 * панель Remnawave (subs.gojihub.xyz) недоступны, в приложении всё равно показывается последний
 * известный список серверов и можно подключиться к последнему выбранному, а не пустой экран.
 * Не замена ClientProfileStorage (тот хранит только connectPayload для подключения) — здесь
 * нужны ещё name/geo/uuid для самого списка на экране "Серверы" и текущей карточки на "Главной".
 *
 * Шифруется тем же Keystore-механизмом, что и ClientProfileStorage/TokenManager — здесь тоже
 * лежит connectPayload/uuid каждого узла, то есть фактически рабочие учётные данные подписки.
 */
object NodeListCache {
    private const val FILE_NAME = "servers_cache.json"
    private const val KEY_NODES = "nodes"
    private const val KEY_SELECTED = "selected_id"
    private const val KEY_SUBSCRIPTION_LINK = "subscription_link"

    data class Cached(val nodes: List<VlessNode>, val selectedId: String?, val subscriptionLink: String?)

    /** [subscriptionLink] — та же ссылка, что обычно приходит от getSubscriptions() (gojihub.xyz)
     *  на каждый refresh(); кэшируем и её отдельно от узлов, чтобы при недоступности НАШЕГО
     *  шоп-бэкенда (см. SubscriptionRepository.refresh()) можно было всё равно попробовать
     *  обновить сам список серверов напрямую с последней известной ссылки — subs.gojihub.xyz
     *  (Remnawave) технически независим от gojihub.xyz и может быть доступен, даже когда сам
     *  шоп-сайт заблокирован/недоступен на конкретной сети. */
    fun save(context: Context, nodes: List<VlessNode>, selectedId: String?, subscriptionLink: String? = null) {
        val array = JSONArray()
        nodes.forEach { array.put(toJson(it)) }
        val root = JSONObject().put(KEY_NODES, array).put(KEY_SELECTED, selectedId ?: JSONObject.NULL)
        // subscriptionLink не передан (например select() просто перезаписывает selectedId) —
        // сохраняем прежнее значение, а не затираем null'ом то, что уже было на диске.
        val linkToSave = subscriptionLink ?: load(context)?.subscriptionLink
        root.put(KEY_SUBSCRIPTION_LINK, linkToSave ?: JSONObject.NULL)
        runCatching {
            val target = file(context)
            if (target.exists()) target.delete() // EncryptedFile не даёт открыть на запись существующий файл
            encryptedFile(context, target).openFileOutput().use { it.write(root.toString().toByteArray()) }
        }
    }

    fun load(context: Context): Cached? = runCatching {
        val target = file(context)
        if (!target.exists()) return null
        val bytes = encryptedFile(context, target).openFileInput().use { it.readBytes() }
        val root = JSONObject(bytes.toString(Charsets.UTF_8))
        val array = root.getJSONArray(KEY_NODES)
        val nodes = (0 until array.length()).map { fromJson(array.getJSONObject(it)) }
        if (nodes.isEmpty()) return null
        Cached(
            nodes,
            root.optString(KEY_SELECTED).takeIf { it.isNotBlank() },
            root.optString(KEY_SUBSCRIPTION_LINK).takeIf { it.isNotBlank() }
        )
    }.getOrNull()

    private fun encryptedFile(context: Context, file: File): EncryptedFile {
        val masterKey = MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build()
        return EncryptedFile.Builder(context, file, masterKey, EncryptedFile.FileEncryptionScheme.AES256_GCM_HKDF_4KB).build()
    }

    private fun toJson(node: VlessNode): JSONObject = JSONObject().apply {
        put("id", node.id)
        put("name", node.name)
        put("host", node.host)
        put("port", node.port)
        put("connectPayload", node.connectPayload)
        put("uuid", node.uuid ?: JSONObject.NULL)
        put("geo", node.geo?.let { geo ->
            JSONObject()
                .put("country", geo.country)
                .put("lat", geo.lat)
                .put("lon", geo.lon)
                .put("lang", geo.lang)
                .put("code", geo.code)
                .put("ruName", geo.ruName)
                .put("ruPrep", geo.ruPrep)
                .put("city", geo.city)
                .put("enCity", geo.enCity)
                .put("zhName", geo.zhName)
                .put("zhCity", geo.zhCity)
        } ?: JSONObject.NULL)
    }

    private fun fromJson(json: JSONObject): VlessNode {
        val geo = json.optJSONObject("geo")?.let {
            CountryGeo(
                country = it.getString("country"),
                lat = it.getDouble("lat"),
                lon = it.getDouble("lon"),
                lang = it.getString("lang"),
                code = it.getString("code"),
                ruName = it.getString("ruName"),
                ruPrep = it.getString("ruPrep"),
                city = it.getString("city"),
                enCity = it.optString("enCity").ifBlank { it.getString("city") },
                zhName = it.optString("zhName").ifBlank { it.getString("country") },
                zhCity = it.optString("zhCity").ifBlank { it.getString("city") }
            )
        }
        return VlessNode(
            id = json.getString("id"),
            name = json.getString("name"),
            host = json.getString("host"),
            port = json.getInt("port"),
            connectPayload = json.getString("connectPayload"),
            geo = geo,
            uuid = json.optString("uuid").takeIf { it.isNotBlank() }
        )
    }

    private fun file(context: Context) = File(context.filesDir, FILE_NAME)
}
