package xyz.gojihub.vpn.network

import okhttp3.Headers

/** Set-Cookie может встречаться несколько раз в одном ответе (rw_session_token +
 *  rw_refresh_token) — берём только нужное по имени, до первой ';' (остальное —
 *  атрибуты куки: Path/Expires/HttpOnly/Secure/SameSite, не часть значения). Общая для
 *  AuthRepository (email-вход) и TokenAuthenticator (обновление сессии по 401). */
fun extractCookieValue(headers: Headers, cookieName: String): String? =
    headers.values("Set-Cookie")
        .firstOrNull { it.startsWith("$cookieName=") }
        ?.substringAfter("$cookieName=")
        ?.substringBefore(";")
