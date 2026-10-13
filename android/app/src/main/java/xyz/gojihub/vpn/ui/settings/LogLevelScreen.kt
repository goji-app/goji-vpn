package xyz.gojihub.vpn.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.RadioButton
import androidx.compose.material3.RadioButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.util.LogLevel

/** Отдельное подменю (не радиокнопки прямо в общих Настройках) — так же, как и
 *  PingSettingsScreen: редко используемая настройка не должна загромождать основной экран. */
@Composable
fun LogLevelScreen(onBack: () -> Unit, viewModel: SettingsViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsState()

    Column(
        Modifier
            .fillMaxSize()
            .background(GodjiColors.Background)
            .verticalScroll(rememberScrollState())
            .padding(18.dp, 18.dp, 18.dp, 10.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackButton(onClick = onBack)
            Text(Loc.s.logLevelTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp)
        }

        SettingsSection(Loc.s.settingsLogLevel) {
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                LogLevelRow(Loc.s.logLevelNone, LogLevel.NONE, state.logLevel, viewModel::setLogLevel)
                LogLevelRow(Loc.s.logLevelError, LogLevel.ERROR, state.logLevel, viewModel::setLogLevel)
                LogLevelRow(Loc.s.logLevelWarning, LogLevel.WARNING, state.logLevel, viewModel::setLogLevel)
                LogLevelRow(Loc.s.logLevelInfo, LogLevel.INFO, state.logLevel, viewModel::setLogLevel)
                LogLevelRow(Loc.s.logLevelDebug, LogLevel.DEBUG, state.logLevel, viewModel::setLogLevel)
            }
        }

        Spacer(Modifier.height(4.dp))
    }
}

@Composable
private fun LogLevelRow(label: String, level: LogLevel, selected: LogLevel, onSelect: (LogLevel) -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable { onSelect(level) }
            .padding(vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        RadioButton(
            selected = selected == level,
            onClick = { onSelect(level) },
            colors = RadioButtonDefaults.colors(selectedColor = GodjiColors.TealDeep, unselectedColor = GodjiColors.ButtonBorder)
        )
        Text(label, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Medium, fontSize = 13.sp)
    }
}
