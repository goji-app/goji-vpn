package xyz.gojihub.vpn.subscription

import android.content.Context
import androidx.security.crypto.EncryptedFile
import androidx.security.crypto.MasterKey
import java.io.File

/**
 * Полные клиентские JSON-профили узлов подписки (dns/routing/outbounds, включая VLESS UUID —
 * фактический "пароль" для подключения к серверу в обход HWID-гейта subs.gojihub.xyz, если его
 * извлечь) — раньше держались только в памяти (StateFlow в SubscriptionRepository) и
 * вытягивались из сети заново при каждом запуске; теперь при каждом обновлении подписки
 * сохраняются на диск отдельно от рантайм-состояния для офлайн-доступа.
 *
 * Шифруем тем же Keystore-механизмом (Jetpack Security), что и токен авторизации в
 * TokenManager — сами по себе файлы лежат в приватной директории приложения (недоступны другим
 * приложениям без root в обычном режиме), но на скомпрометированном/рутованном устройстве
 * голый файл можно было прочитать напрямую; со сквозным AES256-GCM-шифрованием, привязанным к
 * аппаратному Keystore этого устройства, требуется уже сам ключ шифрования, а не просто доступ
 * к файловой системе.
 */
object ClientProfileStorage {
    private const val DIR_NAME = "client_profiles"

    fun saveAll(context: Context, nodes: List<VlessNode>) {
        val dir = dir(context)
        if (!dir.exists()) dir.mkdirs()
        // Убираем файлы узлов, которых больше нет в подписке (ротация серверов на бэкенде).
        val keepNames = nodes.map { fileNameFor(it.id) }.toSet()
        dir.listFiles()?.forEach { file -> if (file.name !in keepNames) file.delete() }
        nodes.forEach { node ->
            runCatching { writeEncrypted(context, fileFor(context, node.id), node.connectPayload) }
        }
    }

    fun read(context: Context, nodeId: String): String? =
        runCatching { readEncrypted(context, fileFor(context, nodeId)) }.getOrNull()

    fun fileFor(context: Context, nodeId: String): File = File(dir(context), fileNameFor(nodeId))

    private fun writeEncrypted(context: Context, file: File, text: String) {
        if (file.exists()) file.delete() // EncryptedFile не даёт открыть на запись существующий файл
        encryptedFile(context, file).openFileOutput().use { it.write(text.toByteArray()) }
    }

    private fun readEncrypted(context: Context, file: File): String? {
        if (!file.exists()) return null
        return encryptedFile(context, file).openFileInput().use { it.readBytes() }.toString(Charsets.UTF_8)
    }

    private fun encryptedFile(context: Context, file: File): EncryptedFile {
        val masterKey = MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build()
        return EncryptedFile.Builder(context, file, masterKey, EncryptedFile.FileEncryptionScheme.AES256_GCM_HKDF_4KB).build()
    }

    private fun fileNameFor(nodeId: String) = "$nodeId.json"

    private fun dir(context: Context) = File(context.filesDir, DIR_NAME)
}
