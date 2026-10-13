package xyz.gojihub.vpn.subscription

import android.content.Context
import androidx.security.crypto.EncryptedFile
import androidx.security.crypto.MasterKey
import com.squareup.moshi.Moshi
import com.squareup.moshi.Types
import dagger.hilt.android.qualifiers.ApplicationContext
import org.json.JSONObject
import xyz.gojihub.vpn.network.models.BroadcastDto
import xyz.gojihub.vpn.network.models.DeviceDto
import xyz.gojihub.vpn.network.models.PartnerStatusResponse
import xyz.gojihub.vpn.network.models.PlansResponse
import xyz.gojihub.vpn.network.models.ReferralsResponse
import xyz.gojihub.vpn.network.models.SubscriptionInfo
import java.io.File
import java.lang.reflect.Type
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Последние успешно полученные данные аккаунта — подписка, новости, тарифы, рефералы,
 * партнёрка, устройства — на диске, как список серверов в [NodeListCache]. Экраны («Главная»,
 * «Подписка», виджеты) показывают их сразу, без ожидания сети, а обновление идёт в фоне и при
 * сбое (нет интернета, VPN переподключается) просто оставляет прежнее, а не пустой экран.
 *
 * Каждый раздел — отдельный файл с моментом сохранения ([Entry.savedAt]), чтобы решать, пора ли
 * его обновлять. Шифруется тем же Keystore-ключом, что NodeListCache: в подписке лежит ссылка
 * подписки, то есть рабочие учётные данные.
 */
@Singleton
class AccountCache @Inject constructor(
    @ApplicationContext private val context: Context,
    moshi: Moshi
) {
    data class Entry<T>(val value: T, val savedAt: Long)

    enum class Key(val file: String) {
        SUBSCRIPTION("account_subscription.json"),
        BROADCASTS("account_broadcasts.json"),
        PLANS("account_plans.json"),
        REFERRALS("account_referrals.json"),
        PARTNER("account_partner.json"),
        DEVICES("account_devices.json")
    }

    private val adapters: Map<Key, com.squareup.moshi.JsonAdapter<Any>> = mapOf(
        Key.SUBSCRIPTION to SubscriptionInfo::class.java,
        Key.BROADCASTS to Types.newParameterizedType(List::class.java, BroadcastDto::class.java),
        Key.PLANS to PlansResponse::class.java,
        Key.REFERRALS to ReferralsResponse::class.java,
        Key.PARTNER to PartnerStatusResponse::class.java,
        Key.DEVICES to Types.newParameterizedType(List::class.java, DeviceDto::class.java)
    ).mapValues { (_, type: Type) -> moshi.adapter<Any>(type) }

    fun <T : Any> save(key: Key, value: T) {
        runCatching {
            val json = adapters.getValue(key).toJson(value)
            val root = JSONObject().put("at", System.currentTimeMillis()).put("data", json)
            val target = File(context.filesDir, key.file)
            if (target.exists()) target.delete() // EncryptedFile не открывает существующий файл на запись
            encrypted(target).openFileOutput().use { it.write(root.toString().toByteArray()) }
        }
    }

    @Suppress("UNCHECKED_CAST")
    fun <T : Any> load(key: Key): Entry<T>? = runCatching {
        val target = File(context.filesDir, key.file)
        if (!target.exists()) return null
        val text = encrypted(target).openFileInput().use { it.readBytes() }.toString(Charsets.UTF_8)
        val root = JSONObject(text)
        val value = adapters.getValue(key).fromJson(root.getString("data")) as? T ?: return null
        Entry(value, root.optLong("at"))
    }.getOrNull()

    fun clear() {
        Key.entries.forEach { runCatching { File(context.filesDir, it.file).delete() } }
    }

    private fun encrypted(file: File): EncryptedFile {
        val masterKey = MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build()
        return EncryptedFile.Builder(context, file, masterKey, EncryptedFile.FileEncryptionScheme.AES256_GCM_HKDF_4KB).build()
    }
}
