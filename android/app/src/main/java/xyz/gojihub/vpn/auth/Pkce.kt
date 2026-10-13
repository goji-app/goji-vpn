package xyz.gojihub.vpn.auth

import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom

/**
 * Генерация пары code_verifier / code_challenge для PKCE (RFC 7636).
 * Используется для Native OAuth (Google/Yandex/Telegram) через /api/auth/{provider}/start
 * и /api/auth/native/exchange — см. настройку "Мобильные приложения" в панели.
 */
object Pkce {

    fun generateVerifier(): String {
        val bytes = ByteArray(64)
        SecureRandom().nextBytes(bytes)
        return encode(bytes)
    }

    fun challengeFor(verifier: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII))
        return encode(digest)
    }

    private fun encode(bytes: ByteArray): String =
        Base64.encodeToString(bytes, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)
}
