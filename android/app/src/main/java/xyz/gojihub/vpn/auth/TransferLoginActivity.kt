package xyz.gojihub.vpn.auth

import android.app.AlertDialog
import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.login.transferErrorText
import javax.inject.Inject

/**
 * Ловит godjivpn://transfer?... — QR-код переноса входа, отсканированный СИСТЕМНОЙ камерой (не
 * встроенным сканером на экране входа). Раз ссылка может прийти откуда угодно, вход не
 * выполняется молча: сначала явное подтверждение пользователя — иначе чужая ссылка могла бы
 * тихо переключить приложение на чужой аккаунт.
 */
@AndroidEntryPoint
class TransferLoginActivity : ComponentActivity() {

    @Inject lateinit var authRepository: AuthRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val raw = intent?.dataString
        if (raw.isNullOrBlank()) {
            finish()
            return
        }
        val message = if (authRepository.isLoggedIn()) Loc.f.transferConfirmReplace else Loc.f.transferConfirmText
        // Прозрачная активити без собственной темы — иначе диалог рисуется в стиле Holo.
        val dialogTheme = if (xyz.gojihub.vpn.ui.theme.GodjiColors.isDark) android.R.style.Theme_DeviceDefault_Dialog_Alert
            else android.R.style.Theme_DeviceDefault_Light_Dialog_Alert
        AlertDialog.Builder(this, dialogTheme)
            .setTitle(Loc.f.transferConfirmTitle)
            .setMessage(message)
            .setPositiveButton(Loc.f.transferConfirmYes) { _, _ -> login(raw) }
            .setNegativeButton(Loc.f.transferCancel) { _, _ -> finish() }
            .setOnCancelListener { finish() }
            .show()
    }

    private fun login(raw: String) {
        lifecycleScope.launch {
            val error = authRepository.loginWithTransfer(raw)
            if (error != null) {
                Toast.makeText(this@TransferLoginActivity, transferErrorText(error), Toast.LENGTH_LONG).show()
                finish()
                return@launch
            }
            // Как в OAuthCallbackActivity: новый onCreate() MainActivity с актуальным isLoggedIn().
            startActivity(
                Intent(this@TransferLoginActivity, MainActivity::class.java)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
            )
            finish()
        }
    }
}
