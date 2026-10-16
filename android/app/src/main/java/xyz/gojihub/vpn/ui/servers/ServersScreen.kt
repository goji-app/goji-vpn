package xyz.gojihub.vpn.ui.servers

import xyz.gojihub.vpn.ui.theme.RefreshPill
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.StarBorder
import androidx.compose.foundation.selection.toggleable
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.PriorityHigh
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.settings.ServerSort
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.theme.godjiGlassFlat
import xyz.gojihub.vpn.ui.theme.godjiGlassPill

@Composable
fun ServersScreen(viewModel: ServersViewModel = hiltViewModel(), onServerPicked: () -> Unit = {}) {
    val state by viewModel.state.collectAsState()
    val refreshingSubscription by viewModel.refreshingSubscription.collectAsState()
    val refreshMessage by viewModel.refreshMessage.collectAsState()

    // Авто-пинг при каждом заходе на вкладку — composable пересоздаётся при переключении вкладок
    // (NavHost saveState/restoreState держит только ViewModel, не Compose-дерево), поэтому
    // LaunchedEffect(Unit) срабатывает ровно при каждом открытии "Серверов", а не один раз за
    // всё время жизни приложения. Раньше нужно было нажимать "⚡" вручную — выбор шёл вслепую.
    // PingRepository.pingAllInternal() сам не даст двум проверкам наложиться друг на друга.
    LaunchedEffect(Unit) { viewModel.pingAllIfStale() }

    // Эталон: колонка padding 16, gap 12; шапка padding 0 4px, выравнивание по низу.
    Column(
        Modifier
            .fillMaxSize()
            .background(GodjiColors.Background)
            // Со строкой сортировки 8+ узлов уже не всегда влезают на невысокий экран.
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 4.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.Bottom
        ) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(Loc.s.serversTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.Bold, fontSize = 26.sp, lineHeight = 28.sp, letterSpacing = (-0.6).sp)
                Text(Loc.s.serversCount(state.servers.size), color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp)
            }
            Spacer(Modifier.width(10.dp))
            // Одна кнопка вместо «Обновить» + «Пинг»: refreshSubscription() скачивает свежий список
            // и сразу проверяет пинг всех узлов (а при открытии вкладки пинг запускается и сам).
            RefreshPill(
                label = Loc.s.serversRefresh, busy = refreshingSubscription || state.checkingAll,
                onClick = viewModel::refreshSubscription
            )
        }

        SortSegments(state.sort, onSelect = viewModel::setSort)

        AnimatedContent(
            targetState = refreshMessage,
            transitionSpec = { fadeIn(tween(200)) togetherWith fadeOut(tween(150)) },
            label = "refreshBanner"
        ) { msg ->
            if (msg != null) RefreshBanner(msg, onDismiss = viewModel::dismissRefreshMessage)
        }

        Column(Modifier.fillMaxWidth().godjiCard().clip(RoundedCornerShape(26.dp))) {
            state.servers.forEachIndexed { index, node ->
                if (index > 0) HorizontalDivider(thickness = 1.dp, color = GodjiColors.Hair)
                val selected = node.id == state.selectedId
                val rowBg by animateColorAsState(if (selected) GodjiColors.SelBg else Color.Transparent, tween(300), label = "rowBg")
                val pickInteraction = remember { MutableInteractionSource() }
                val pickPressed by pickInteraction.collectIsPressedAsState()
                Row(
                    Modifier.fillMaxWidth().background(rowBg).padding(end = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    // Кнопка выбора (эталон: padding 12 0 12 14, gap 12, при нажатии opacity .6)
                    Row(
                        Modifier
                            .weight(1f)
                            .alpha(if (pickPressed) 0.6f else 1f)
                            .clickable(interactionSource = pickInteraction, indication = null) { viewModel.select(node.id); onServerPicked() }
                            .padding(start = 14.dp, top = 9.dp, bottom = 9.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp)
                    ) {
                        Box(
                            Modifier
                                .size(36.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(GodjiColors.Chip)
                                .border(1.dp, if (selected) GodjiColors.Teal else GodjiColors.CardBorder, RoundedCornerShape(12.dp)),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(node.flag, fontSize = 18.sp)
                        }
                        Text(node.name, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.5.sp, modifier = Modifier.weight(1f))
                        if (selected) {
                            Icon(Icons.Filled.Check, contentDescription = null, tint = GodjiColors.TealDeep, modifier = Modifier.size(18.dp))
                        }
                    }
                    // Избранное — в эталоне его нет, но это рабочая функция приложения, а не
                    // визуальная деталь, поэтому оставлена компактной звёздочкой.
                    // Зона нажатия 44dp вместо 20dp у прежнего символа «☆»; TalkBack слышит переключатель.
                    Box(
                        Modifier
                            .size(44.dp)
                            .clip(CircleShape)
                            .toggleable(
                                value = node.isFavorite,
                                interactionSource = remember { MutableInteractionSource() },
                                indication = null,
                                role = Role.Checkbox
                            ) { viewModel.toggleFavorite(node.id) },
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            if (node.isFavorite) Icons.Filled.Star else Icons.Filled.StarBorder,
                            contentDescription = Loc.f.a11yFavorite,
                            tint = if (node.isFavorite) GodjiColors.Warning else GodjiColors.TextMuted,
                            modifier = Modifier.size(18.dp)
                        )
                    }
                    // Плашка пинга — отдельная кнопка: проверить только этот узел.
                    val pingInteraction = remember { MutableInteractionSource() }
                    val pingPressed by pingInteraction.collectIsPressedAsState()
                    Box(
                        Modifier
                            .scale(if (pingPressed) 0.94f else 1f)
                            .defaultMinSize(minWidth = 70.dp)
                            .height(30.dp)
                            .godjiGlassFlat(RoundedCornerShape(50))
                            .clickable(interactionSource = pingInteraction, indication = null, onClickLabel = Loc.f.a11yPingOne) { viewModel.pingOne(node.id) }
                            .padding(horizontal = 11.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            when {
                                state.checkingId == node.id || (state.checkingAll && node.pingMs == -2) -> "…"
                                node.pingMs == -2 -> Loc.s.serversCheck
                                node.pingMs == -1 -> Loc.s.serversUnavailable
                                else -> Loc.s.serversPingMs(node.pingMs)
                            },
                            color = pingColor(node.pingMs),
                            fontWeight = FontWeight.Bold, fontSize = 11.5.sp
                        )
                    }
                }
            }
        }
    }
}

/** Сортировка списка — тот же сегмент-контрол, что "Язык приложения" в Настройках
 *  (дорожка chip + stroke, бегунок thumb + stroke), только ниже: 32dp, 12sp. */
@Composable
private fun SortSegments(selected: ServerSort, onSelect: (ServerSort) -> Unit) {
    val options = listOf(
        ServerSort.FAVORITES to Loc.f.sortFavorites,
        ServerSort.PING to Loc.f.sortPing,
        ServerSort.NAME to Loc.f.sortName
    )
    BoxWithConstraints(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(50))
            .background(GodjiColors.Chip)
            .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
            .padding(3.dp)
    ) {
        val slotWidth = maxWidth / options.size
        val index = options.indexOfFirst { it.first == selected }.coerceAtLeast(0)
        val thumbX by animateDpAsState(slotWidth * index, spring(dampingRatio = 0.62f, stiffness = 380f), label = "sortThumb")
        Box(
            Modifier
                .offset(x = thumbX)
                .width(slotWidth)
                .height(32.dp)
                .clip(RoundedCornerShape(50))
                .background(GodjiColors.Thumb)
                .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
        )
        Row(Modifier.fillMaxWidth()) {
            options.forEachIndexed { i, (sort, label) ->
                Box(
                    Modifier
                        .weight(1f)
                        .height(32.dp)
                        .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { onSelect(sort) },
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        label,
                        color = if (i == index) GodjiColors.TextPrimary else GodjiColors.TextSecondary,
                        fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1
                    )
                }
            }
        }
    }
}

/** Круглая стеклянная кнопка 40dp с подписью 9.5sp под ней (gap 3) — "Обновить"/"Пинг". */
/** Эталон: padding 11/13, радиус 20, круг 24dp с галочкой, текст 12sp, крестик 22dp на hair. */
@Composable
private fun RefreshBanner(message: RefreshMessage, onDismiss: () -> Unit) {
    val bg = if (!message.isError) GodjiColors.TealTint else GodjiColors.TerracottaTint
    Row(
        Modifier
            .fillMaxWidth()
            .godjiGlassPill(RoundedCornerShape(20.dp), tint = bg)
            .padding(horizontal = 13.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Box(
            Modifier.size(24.dp).clip(CircleShape).background(if (message.isError) GodjiColors.Terracotta else GodjiColors.Teal),
            contentAlignment = Alignment.Center
        ) {
            Icon(if (message.isError) Icons.Filled.PriorityHigh else Icons.Filled.Check, contentDescription = null, tint = GodjiColors.Surface, modifier = Modifier.size(13.dp))
        }
        Text(if (message.isError) Loc.s.refreshFail else Loc.s.refreshOk, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 12.sp, modifier = Modifier.weight(1f))
        Box(
            Modifier
                .size(40.dp)
                .clip(CircleShape)
                .clickable(role = Role.Button, onClickLabel = Loc.f.qrClose) { onDismiss() }
                .semantics { contentDescription = Loc.f.qrClose },
            contentAlignment = Alignment.Center
        ) {
            Box(Modifier.size(22.dp).clip(CircleShape).background(GodjiColors.Hair), contentAlignment = Alignment.Center) {
                Icon(Icons.Filled.Close, contentDescription = null, tint = GodjiColors.TextPrimary, modifier = Modifier.size(13.dp))
            }
        }
    }
}

/** Цвета пинга: не проверен — sub, недоступен/медленный — danger, быстрый — accentInk,
 *  средний — #C98B12. Пинг меряется через туннель до узла в Европе: обычные значения 300–1800 мс, поэтому пороги
 *  500/1000 мс (как на Windows, macOS и iOS); при прежних 40/90 мс красным был каждый узел. */
private const val PING_GOOD_MS = 500
private const val PING_MID_MS = 1000

private fun pingColor(pingMs: Int): Color = when {
    pingMs == -2 -> GodjiColors.TextSecondary
    pingMs < 0 -> GodjiColors.Danger
    pingMs < PING_GOOD_MS -> GodjiColors.TealDeep
    pingMs < PING_MID_MS -> Color(0xFFC98B12)
    else -> GodjiColors.Danger
}
