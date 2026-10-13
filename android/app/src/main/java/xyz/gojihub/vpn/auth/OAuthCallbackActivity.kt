package xyz.gojihub.vpn.auth

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.i18n.Loc
import javax.inject.Inject

/**
 * Ловит deeplink godjivpn://oauth2redirect?code=...&provider=..., на который сервер
 * редиректит браузер после серверного OAuth-callback (Google/Yandex/Telegram).
 * Код одноразовый и обменивается на JWT через /api/auth/native/exchange (PKCE).
 */
@AndroidEntryPoint
class OAuthCallbackActivity : ComponentActivity() {

    @Inject lateinit var authRepository: AuthRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handleIntent(intent)
    }

    private fun handleIntent(intent: Intent?) {
        val uri = intent?.data
        val code = uri?.getQueryParameter("code")
        val provider = uri?.getQueryParameter("provider") ?: authRepository.pendingProvider()

        if (code.isNullOrBlank() || provider.isNullOrBlank()) {
            // Раньше тут был тихий finish() без единого сообщения — снаружи это выглядело как
            // "зависание": пользователь подтверждает вход, а приложение как будто ничего не
            // делает. Теперь хотя бы явно говорим, что сессию нужно начать заново, а не
            // притворяемся, что всё в порядке.
            Toast.makeText(this, Loc.s.errorOAuthToastRetry, Toast.LENGTH_LONG).show()
            finish()
            return
        }

        lifecycleScope.launch {
            val result = authRepository.completeOAuth(provider, code)
            if (result is AuthResult.Error) {
                Toast.makeText(this@OAuthCallbackActivity, result.message, Toast.LENGTH_LONG).show()
            }
            // MainActivity почти всегда уже жива в фоне (пользователь на ней стартовал OAuth) —
            // singleTask + CLEAR_TOP просто вернули бы ту же старую живую копию с уже
            // просчитанным при её onCreate() "не залогинен" без повторного onCreate(), и юзер
            // визуально "не мог войти", хотя токен на самом деле уже сохранён. NEW_TASK +
            // CLEAR_TASK гарантируют настоящий новый onCreate() с актуальным isLoggedIn().
            startActivity(
                Intent(this@OAuthCallbackActivity, MainActivity::class.java)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
            )
            finish()
        }
    }
}
