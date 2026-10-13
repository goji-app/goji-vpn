package xyz.gojihub.vpn.vpn

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.geo.CountryGeoLookup
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.widget.widgetEntryPoint

/**
 * Ярлык "Подключиться: <страна>" в меню долгого нажатия на иконку приложения — поднимает
 * туннель на ПОСЛЕДНЕМ узле, к которому реально подключались (SettingsRepository.lastConnect*,
 * тот же, что использует Always-on VPN), не открывая интерфейс приложения.
 */
object LastNodeShortcut {
    private const val ID = "connect_last_node"
    const val ACTION = "xyz.gojihub.vpn.action.CONNECT_LAST_NODE"

    /** Публикует/обновляет ярлык с актуальным названием последнего узла. */
    suspend fun publish(context: Context) {
        val settings = context.widgetEntryPoint().settingsRepository()
        val rawLabel = settings.lastConnectLabelNow()
        if (settings.lastConnectPayloadNow().isNullOrBlank()) return
        val country = rawLabel?.takeIf { it.isNotBlank() }?.let { displayName(it) }
        val shortcut = ShortcutInfoCompat.Builder(context, ID)
            .setShortLabel(country ?: Loc.f.shortcutLastNodeShort)
            .setLongLabel(country?.let(Loc.f.shortcutConnectTo) ?: Loc.f.shortcutLastNodeLong)
            .setIcon(IconCompat.createWithResource(context, R.drawable.ic_shortcut_connect))
            .setIntent(Intent(context, LastNodeShortcutActivity::class.java).setAction(ACTION))
            .build()
        runCatching { ShortcutManagerCompat.pushDynamicShortcut(context, shortcut) }
    }

    fun displayName(label: String): String =
        CountryGeoLookup.find(label)?.displayName(Loc.lang) ?: label
}

/** Прозрачный трамплин ярлыка: без собственного UI, закрывается сразу после команды. Если
 *  разрешение на VPN ещё не выдано — показывает системный диалог и подключается после
 *  согласия. */
class LastNodeShortcutActivity : ComponentActivity() {

    private var pendingPayload: String? = null
    private var pendingLabel: String = ""

    private val vpnPermission = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val payload = pendingPayload
        if (result.resultCode == RESULT_OK && payload != null) connectAndFinish(payload, pendingLabel) else finish()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        lifecycleScope.launch {
            val settings = widgetEntryPoint().settingsRepository()
            val payload = settings.lastConnectPayloadNow()
            val label = settings.lastConnectLabelNow().orEmpty()
            if (payload.isNullOrBlank()) {
                // Ещё ни разу не подключались — просто открываем приложение.
                startActivity(Intent(this@LastNodeShortcutActivity, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                finish()
                return@launch
            }
            if (GodjiVpnService.isRunning.value) {
                Toast.makeText(this@LastNodeShortcutActivity, Loc.f.shortcutAlreadyOn, Toast.LENGTH_SHORT).show()
                finish()
                return@launch
            }
            val prepare = android.net.VpnService.prepare(this@LastNodeShortcutActivity)
            if (prepare == null) {
                connectAndFinish(payload, label)
            } else {
                pendingPayload = payload
                pendingLabel = label
                vpnPermission.launch(prepare)
            }
        }
    }

    private fun connectAndFinish(payload: String, label: String) {
        if (VpnLauncher.connect(this, payload, label)) {
            val name = label.takeIf { it.isNotBlank() }?.let(LastNodeShortcut::displayName)
            Toast.makeText(this, name?.let(Loc.f.shortcutConnecting) ?: Loc.f.tileConnecting, Toast.LENGTH_SHORT).show()
        }
        finish()
    }
}
