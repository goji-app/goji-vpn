package xyz.gojihub.vpn.ui.settings

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.settings.PerAppProxyMode
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiGlassFlat
import xyz.gojihub.vpn.ui.util.BackButton

/** "Прокси для выбранных приложений" — тот же приём, что Per-app Proxy у Happ: ВЫКЛ (всем
 *  приложениям одинаково), ВКЛ (через VPN идут только выбранные, остальное напрямую), Обход
 *  (выбранные идут напрямую, остальное через VPN). Список — все установленные пакеты, системные
 *  помечены "*", как и у Happ (см. AppTunnelingViewModel). */
@Composable
fun AppTunnelingScreen(onBack: () -> Unit, viewModel: AppTunnelingViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsState()

    Column(Modifier.fillMaxSize().background(GodjiColors.Background)) {
        Row(
            Modifier.fillMaxWidth().padding(18.dp, 18.dp, 18.dp, 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            BackButton(onClick = onBack)
            Text(Loc.s.appTunnelingTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 21.sp)
        }

        LazyColumn(
            Modifier.fillMaxSize().padding(horizontal = 18.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            item {
                Row(
                    Modifier.fillMaxWidth().background(GodjiColors.Chip, RoundedCornerShape(14.dp)).padding(4.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    ModeTab(Loc.s.appTunnelingModeOff, PerAppProxyMode.OFF, state.mode, Modifier.weight(1f), viewModel::setMode)
                    ModeTab(Loc.s.appTunnelingModeOn, PerAppProxyMode.ALLOW, state.mode, Modifier.weight(1f), viewModel::setMode)
                    ModeTab(Loc.s.appTunnelingModeBypass, PerAppProxyMode.BYPASS, state.mode, Modifier.weight(1f), viewModel::setMode)
                }
            }
            item {
                val desc = when (state.mode) {
                    PerAppProxyMode.OFF -> Loc.s.appTunnelingModeOffDesc
                    PerAppProxyMode.ALLOW -> Loc.s.appTunnelingModeOnDesc
                    PerAppProxyMode.BYPASS -> Loc.s.appTunnelingModeBypassDesc
                }
                Text(
                    desc,
                    color = GodjiColors.TextSecondary,
                    fontSize = 11.5.sp,
                    lineHeight = 15.sp,
                    modifier = Modifier
                        .fillMaxWidth()
                        .godjiGlassFlat(RoundedCornerShape(14.dp))
                        .padding(12.dp)
                )
            }
            // Список виден во всех трёх режимах, включая ВЫКЛ — так же, как у Happ (там тоже
            // можно заранее отметить приложения, ещё не переключившись на ВКЛ/Обход). Сама
            // отметка ни на что не влияет, пока не выбран режим ВКЛ или Обход (см.
            // GodjiVpnService.establishTunnel), но выбор одного отдельного экрана удобнее
            // делать до переключения вкладки, а не после.
            item {
                OutlinedTextField(
                    value = state.query,
                    onValueChange = viewModel::setQuery,
                    placeholder = { Text(Loc.s.appTunnelingSearchPlaceholder) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
            }
            item {
                Text(
                    Loc.s.appTunnelingAppsListTitle,
                    color = GodjiColors.TextSecondary,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 11.sp,
                    modifier = Modifier.padding(top = 4.dp)
                )
            }
            if (state.loading) {
                item {
                    Box(Modifier.fillMaxWidth().padding(vertical = 24.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator(color = GodjiColors.TealDeep)
                    }
                }
            } else {
                items(state.apps, key = { it.packageName }) { app ->
                    AppRow(app, checked = app.packageName in state.selected, onToggle = { viewModel.toggleApp(app.packageName) })
                }
            }
            item { Spacer(Modifier.height(8.dp)) }
        }
    }
}

@Composable
private fun ModeTab(label: String, mode: PerAppProxyMode, selected: PerAppProxyMode, modifier: Modifier, onSelect: (PerAppProxyMode) -> Unit) {
    val active = mode == selected
    Box(
        modifier
            .height(34.dp)
            .clip(RoundedCornerShape(50))
            .background(if (active) GodjiColors.Ink else Color.Transparent)
            .clickable { onSelect(mode) },
        contentAlignment = Alignment.Center
    ) {
        Text(
            label, color = if (active) GodjiColors.Surface else GodjiColors.TextSecondary,
            fontWeight = FontWeight.Bold, fontSize = 12.sp,
            maxLines = 1, overflow = TextOverflow.Ellipsis
        )
    }
}

@Composable
private fun AppRow(app: InstalledAppUi, checked: Boolean, onToggle: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onToggle)
            .padding(vertical = 2.dp, horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Box(
            Modifier.size(26.dp).clip(CircleShape).background(GodjiColors.Chip),
            contentAlignment = Alignment.Center
        ) {
            app.icon?.let { Image(it, contentDescription = null, modifier = Modifier.size(20.dp)) }
        }
        Column(Modifier.weight(1f)) {
            Text(
                if (app.isSystem) "* ${app.label}" else app.label,
                color = GodjiColors.TextPrimary,
                fontWeight = FontWeight.Medium,
                fontSize = 12.sp,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Text(app.packageName, color = GodjiColors.TextSecondary, fontSize = 9.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        // scale — Material3 иначе резервирует под Checkbox минимальную зону касания 48dp
        // независимо от заданных размеров, из-за чего список выглядел рыхлым при сотнях строк.
        Checkbox(
            checked = checked,
            onCheckedChange = { onToggle() },
            colors = CheckboxDefaults.colors(checkedColor = GodjiColors.TealDeep),
            modifier = Modifier.scale(0.8f)
        )
    }
}
