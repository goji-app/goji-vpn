package xyz.gojihub.vpn.ui.globe

import android.os.Handler
import android.os.Looper
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import xyz.gojihub.vpn.globe.GlobeNode
import xyz.gojihub.vpn.globe.GlobeTheme
import xyz.gojihub.vpn.globe.GojiGlobeView
import xyz.gojihub.vpn.ui.theme.GodjiColors

/** Compose-обёртка над OpenGL-глобусом — как у <goji-globe status node> в макете. */
@Composable
fun GojiGlobe(
    status: String,
    node: GlobeNode?,
    // Русское название страны для плавающей подписи — GlobeNode.country хранит английское
    // имя (должно совпадать с properties.name в geo_globe.json для подсветки полигона),
    // поэтому для текста, который видит пользователь, берём отдельно переданный перевод.
    label: String = "",
    // GLOBE.md §7 — спутники показываются только на экране входа (LoginScreen передаёт true).
    satellites: Boolean = false,
    modifier: Modifier = Modifier
) {
    var labelVisible by remember { mutableStateOf(false) }
    var labelX by remember { mutableFloatStateOf(0f) }
    var labelY by remember { mutableFloatStateOf(0f) }
    val mainHandler = remember { Handler(Looper.getMainLooper()) }
    // Читаем здесь (не только внутри update{}) — иначе GojiGlobe не перекомпонуется при смене
    // темы в Настройках, и глобус останется в прежней (например светлой) палитре до следующего
    // изменения status/node.
    val dark = GodjiColors.isDark

    BoxWithConstraints(modifier.fillMaxSize()) {
        AndroidView(
            factory = { ctx ->
                GojiGlobeView(ctx).apply {
                    goji.onLabelUpdate = { visible, x, y, _ ->
                        mainHandler.post {
                            labelVisible = visible; labelX = x; labelY = y
                        }
                    }
                }
            },
            update = { view ->
                view.goji.status = status
                view.goji.currentNode = node
                view.goji.theme = if (dark) GlobeTheme.Dark else GlobeTheme.Light
                view.goji.satellites = satellites
            },
            modifier = Modifier.fillMaxSize()
        )

        if (labelVisible && label.isNotEmpty()) {
            val xDp = maxWidth * labelX
            val yDp = maxHeight * labelY
            Text(
                text = label,
                color = GodjiColors.TextPrimary,
                fontSize = 11.sp,
                modifier = Modifier
                    .offset(x = xDp - 40.dp, y = yDp - 34.dp)
                    .background(GodjiColors.Surface.copy(alpha = 0.94f), RoundedCornerShape(10.dp))
                    .padding(horizontal = 9.dp, vertical = 5.dp)
            )
        }
    }
}
