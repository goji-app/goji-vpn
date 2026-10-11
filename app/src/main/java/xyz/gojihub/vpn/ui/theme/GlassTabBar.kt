package xyz.gojihub.vpn.ui.theme

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.background
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Иконки вкладок — те же SVG-пути, что в эталоне (24×24, обводка 2). */
object GojiTabIcons {
    private fun icon(name: String, vararg paths: String): ImageVector =
        ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).apply {
            paths.forEach {
                addPath(
                    pathData = addPathNodes(it), fill = null, stroke = SolidColor(Color.Black),
                    strokeLineWidth = 2f, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round
                )
            }
        }.build()

    val Home = icon("home", "M12 3l7 3v5.5c0 4.3-2.9 8.1-7 9.5-4.1-1.4-7-5.2-7-9.5V6l7-3Z", "M9.2 12.2l2 2 3.6-4")
    val Servers = icon("servers", "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0", "M3 12h18", "M12 3c2.5 2.6 2.5 15 0 18M12 3c-2.5 2.6-2.5 15 0 18")
    val Plans = icon("plans", "M6 5.5h12a3 3 0 0 1 3 3v7a3 3 0 0 1 -3 3H6a3 3 0 0 1 -3 -3v-7a3 3 0 0 1 3 -3Z", "M3 10h18")
    val Settings = icon("settings", "M4 7h10M18 7h2M4 17h4M12 17h8", "M13.8 7a2.2 2.2 0 1 0 4.4 0a2.2 2.2 0 1 0 -4.4 0", "M7.8 17a2.2 2.2 0 1 0 4.4 0a2.2 2.2 0 1 0 -4.4 0")
}

data class GlassTab(val route: String, val label: String, val icon: ImageVector)

/** Короткая нижняя панель навигации M3 Expressive (как в приложениях Android 16/17): во всю
 *  ширину, surfaceContainer, высота 64dp над системной навигацией. У выбранной вкладки под
 *  иконкой — таблетка-индикатор secondaryContainer 56×32, раскрывается пружиной. */
@Composable
fun GlassTabBar(tabs: List<GlassTab>, selectedRoute: String?, onSelect: (GlassTab) -> Unit, modifier: Modifier = Modifier) {
    val idx = tabs.indexOfFirst { it.route == selectedRoute }.coerceAtLeast(0)
    val pill = RoundedCornerShape(50)
    Row(
        modifier
            .fillMaxWidth()
            .background(GodjiColors.SurfaceContainer)
            .navigationBarsPadding()
            .height(64.dp)
    ) {
        tabs.forEachIndexed { i, tab ->
            val selected = i == idx
            val indicator by animateDpAsState(
                if (selected) 56.dp else 24.dp,
                spring(dampingRatio = 0.6f, stiffness = 500f), label = "indicator"
            )
            val iconColor by animateColorAsState(if (selected) GodjiColors.OnSecondaryContainer else GodjiColors.TextSecondary, label = "tabIcon")
            val labelColor by animateColorAsState(if (selected) GodjiColors.TextPrimary else GodjiColors.TextSecondary, label = "tabLabel")
            Column(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .selectable(selected, remember { MutableInteractionSource() }, null, role = Role.Tab) { onSelect(tab) },
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(4.dp, Alignment.CenterVertically)
            ) {
                Box(Modifier.size(56.dp, 32.dp), contentAlignment = Alignment.Center) {
                    if (selected) Box(Modifier.width(indicator).fillMaxHeight().clip(pill).background(GodjiColors.Lens))
                    Icon(tab.icon, contentDescription = null, tint = iconColor, modifier = Modifier.size(22.dp))
                }
                Text(
                    tab.label, color = labelColor, fontSize = 12.sp,
                    fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(horizontal = 2.dp)
                )
            }
        }
    }
}
