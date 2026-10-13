package xyz.gojihub.vpn.ui.settings

import android.Manifest
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.NetworkRulesManager
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton
import javax.inject.Inject

data class NetworkRulesUiState(
    val autoConnect: Boolean = false,
    val disconnectOnTrusted: Boolean = true,
    val trusted: List<String> = emptyList(),
    /** null — не Wi-Fi, "" — Wi-Fi, но имя недоступно. */
    val currentSsid: String? = null
)

@HiltViewModel
class NetworkRulesViewModel @Inject constructor(
    private val settingsRepository: SettingsRepository,
    private val rules: NetworkRulesManager
) : ViewModel() {

    val state: StateFlow<NetworkRulesUiState> = combine(
        settingsRepository.autoConnectOnWifi,
        settingsRepository.disconnectOnTrusted,
        settingsRepository.trustedSsids,
        rules.currentSsid
    ) { auto, disconnect, trusted, ssid ->
        NetworkRulesUiState(auto, disconnect, trusted.sorted(), ssid)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), NetworkRulesUiState())

    fun hasLocationPermission() = rules.hasLocationPermission()
    fun onPermissionResult() = rules.refresh()
    fun setAutoConnect(v: Boolean) = viewModelScope.launch { settingsRepository.setAutoConnectOnWifi(v) }
    fun setDisconnectOnTrusted(v: Boolean) = viewModelScope.launch { settingsRepository.setDisconnectOnTrusted(v) }
    fun trust(ssid: String) = viewModelScope.launch { settingsRepository.addTrustedSsid(ssid) }
    fun untrust(ssid: String) = viewModelScope.launch { settingsRepository.removeTrustedSsid(ssid) }
}

@Composable
fun NetworkRulesScreen(onBack: () -> Unit, viewModel: NetworkRulesViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsState()
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        viewModel.onPermissionResult()
    }

    Column(
        Modifier
            .fillMaxSize()
            .background(GodjiColors.Background)
            .verticalScroll(rememberScrollState())
            .padding(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackButton(onClick = onBack)
            Text(Loc.f.netRulesTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp)
        }

        SectionLabel(Loc.f.netRulesAutoSection)
        RefCard {
            ToggleRow(Loc.f.netRulesAutoConnect, Loc.f.netRulesAutoConnectDesc, state.autoConnect) { viewModel.setAutoConnect(it) }
            Hair()
            ToggleRow(Loc.f.netRulesDisconnectTrusted, Loc.f.netRulesDisconnectTrustedDesc, state.disconnectOnTrusted) { viewModel.setDisconnectOnTrusted(it) }
        }

        // Пояснение до системного запроса (Google Play: prominent disclosure) — зачем
        // приложению геолокация и что с ней происходит.
        var showLocationDisclosure by remember { mutableStateOf(false) }
        if (showLocationDisclosure) {
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { showLocationDisclosure = false },
                title = { Text(Loc.f.locationDisclosureTitle, fontWeight = FontWeight.Bold) },
                text = { Text(Loc.f.locationDisclosureText, fontSize = 13.5.sp, lineHeight = 19.sp) },
                confirmButton = {
                    androidx.compose.material3.TextButton(onClick = {
                        showLocationDisclosure = false
                        permission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
                    }) { Text(Loc.f.locationDisclosureContinue, fontWeight = FontWeight.Bold) }
                },
                dismissButton = {
                    androidx.compose.material3.TextButton(onClick = { showLocationDisclosure = false }) { Text(Loc.f.deleteAccountCancel) }
                }
            )
        }

        SectionLabel(Loc.f.netRulesCurrentSection)
        Row(
            Modifier
                .fillMaxWidth()
                .godjiCard(RoundedCornerShape(24.dp))
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Box(Modifier.size(36.dp).clip(CircleShape).background(GodjiColors.TealTint), contentAlignment = Alignment.Center) {
                Icon(Icons.Filled.Wifi, contentDescription = null, tint = GodjiColors.TealDeep, modifier = Modifier.size(18.dp))
            }
            val ssid = state.currentSsid
            val (title, subtitle) = when {
                ssid == null -> Loc.f.netRulesNotWifi to null
                ssid.isEmpty() -> Loc.f.netRulesHiddenName to
                    (if (viewModel.hasLocationPermission()) Loc.f.netRulesTurnOnLocation else Loc.f.netRulesNeedPermission)
                ssid in state.trusted -> "«$ssid»" to Loc.f.netRulesIsTrusted
                else -> "«$ssid»" to Loc.f.netRulesIsForeign
            }
            RowTexts(title, subtitle, Modifier.weight(1f))
            when {
                ssid == null -> {}
                ssid.isEmpty() && !viewModel.hasLocationPermission() -> ChipButton(Loc.f.netRulesAllow) {
                    showLocationDisclosure = true
                }
                ssid.isNotEmpty() && ssid !in state.trusted -> ChipButton(Loc.f.netRulesTrust) { viewModel.trust(ssid) }
            }
        }

        SectionLabel(Loc.f.netRulesTrustedSection)
        RefCard {
            if (state.trusted.isEmpty()) {
                Text(
                    Loc.f.netRulesTrustedEmpty, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp,
                    lineHeight = 17.sp, modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp)
                )
            }
            state.trusted.forEachIndexed { i, name ->
                if (i > 0) Hair()
                Row(
                    Modifier.fillMaxWidth().padding(start = 14.dp, end = 10.dp, top = 10.dp, bottom = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Text(name, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                    Box(
                        Modifier.size(28.dp).clip(CircleShape).background(GodjiColors.Hair)
                            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { viewModel.untrust(name) },
                        contentAlignment = Alignment.Center
                    ) {
                        Text("×", color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                }
            }
        }

        Text(
            Loc.f.netRulesFootnote, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.sp, lineHeight = 15.5.sp,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
        )
    }
}

@Composable
private fun ChipButton(label: String, onClick: () -> Unit) {
    Box(
        Modifier
            .height(32.dp)
            .clip(RoundedCornerShape(50))
            .background(GodjiColors.Chip)
            .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onClick)
            .padding(horizontal = 14.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(label, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 12.sp)
    }
}
