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
import androidx.compose.ui.graphics.PathFillType
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

/** Иконки вкладок (2026-10-16, одинаковые на всех платформах): дом, метка на карте, билет-подписка,
 *  шестерёнка. Неактивная вкладка — контур, активная — заливка (внутренние вырезы — evenOdd). */
object GojiTabIcons {
    private const val HOME = "M4 10.4L12 4L20 10.4V19A1.6 1.6 0 0 1 18.4 20.6H15V15.4H9V20.6H5.6A1.6 1.6 0 0 1 4 19Z"
    private const val PIN = "M12 21C12 21 5.6 15.5 5.6 10.2A6.4 6.4 0 0 1 18.4 10.2C18.4 15.5 12 21 12 21Z M14.3 10.2A2.3 2.3 0 1 0 9.7 10.2A2.3 2.3 0 1 0 14.3 10.2Z"
    private const val TICKET = "M5.8 5.4H18.2C19.2 5.4 20 6.2 20 7.2V9.6A2.4 2.4 0 0 0 20 14.4V16.8C20 17.8 19.2 18.6 18.2 18.6H5.8C4.8 18.6 4 17.8 4 16.8V14.4A2.4 2.4 0 0 0 4 9.6V7.2C4 6.2 4.8 5.4 5.8 5.4Z"
    private const val TICKET_LINE = "M9 12H15"
    private const val GEAR = "M12.00 4.60L13.87 2.58L15.67 3.13L16.11 5.85L17.23 6.77L19.98 6.67L20.87 8.33L19.26 10.56L19.40 12.00L21.42 13.87L20.87 15.67L18.15 16.11L17.23 17.23L17.33 19.98L15.67 20.87L13.44 19.26L12.00 19.40L10.13 21.42L8.33 20.87L7.89 18.15L6.77 17.23L4.02 17.33L3.13 15.67L4.74 13.44L4.60 12.00L2.58 10.13L3.13 8.33L5.85 7.89L6.77 6.77L6.67 4.02L8.33 3.13L10.56 4.74Z M14.6 12A2.6 2.6 0 1 0 9.4 12A2.6 2.6 0 1 0 14.6 12Z"

    private fun icon(name: String, filled: Boolean, vararg paths: String): ImageVector =
        ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).apply {
            paths.forEach {
                addPath(
                    pathData = addPathNodes(it),
                    fill = if (filled) SolidColor(Color.Black) else null,
                    pathFillType = PathFillType.EvenOdd,
                    stroke = SolidColor(Color.Black),
                    strokeLineWidth = 1.9f, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round
                )
            }
        }.build()

    val Home = icon("home", false, HOME)
    val HomeFilled = icon("home_filled", true, HOME)
    val Servers = icon("servers", false, PIN)
    val ServersFilled = icon("servers_filled", true, PIN)
    val Plans = icon("plans", false, TICKET, TICKET_LINE)
    val PlansFilled = icon("plans_filled", true, TICKET)
    val Settings = icon("settings", false, GEAR)
    val SettingsFilled = icon("settings_filled", true, GEAR)
}

data class GlassTab(val route: String, val label: String, val icon: ImageVector, val selectedIcon: ImageVector = icon)

/** Нижняя панель навигации M3 Expressive: во всю ширину, surfaceContainer, высота [TAB_BAR_HEIGHT]
 *  над системной навигацией (было 64dp, уменьшено 2026-10-16). У выбранной вкладки иконка с заливкой
 *  на таблетке-индикаторе secondaryContainer 48×28, раскрывается пружиной. */
/** Высота нижней панели без системной навигации — ей же отступает контент в MainActivity. */
val TAB_BAR_HEIGHT = 56.dp

@Composable
fun GlassTabBar(tabs: List<GlassTab>, selectedRoute: String?, onSelect: (GlassTab) -> Unit, modifier: Modifier = Modifier) {
    val idx = tabs.indexOfFirst { it.route == selectedRoute }.coerceAtLeast(0)
    val pill = RoundedCornerShape(50)
    Row(
        modifier
            .fillMaxWidth()
            .background(GodjiColors.SurfaceContainer)
            .navigationBarsPadding()
            .height(TAB_BAR_HEIGHT)
    ) {
        tabs.forEachIndexed { i, tab ->
            val selected = i == idx
            val indicator by animateDpAsState(
                if (selected) 48.dp else 24.dp,
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
                verticalArrangement = Arrangement.spacedBy(2.dp, Alignment.CenterVertically)
            ) {
                Box(Modifier.size(48.dp, 28.dp), contentAlignment = Alignment.Center) {
                    if (selected) Box(Modifier.width(indicator).fillMaxHeight().clip(pill).background(GodjiColors.Lens))
                    Icon(if (selected) tab.selectedIcon else tab.icon, contentDescription = null, tint = iconColor, modifier = Modifier.size(20.dp))
                }
                Text(
                    // Одинаковая толщина у всех подписей: выбранная отличается цветом, а не размером.
                    tab.label, color = labelColor, fontSize = 11.sp, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(horizontal = 2.dp)
                )
            }
        }
    }
}
