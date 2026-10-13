package xyz.gojihub.vpn.ui.settings

import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.vpn.GodjiVpnService
import xyz.gojihub.vpn.vpn.VpnLauncher
import javax.inject.Inject

data class BypassDomainsUiState(
    val domains: List<String> = emptyList(),
    val vpnRunning: Boolean = false,
    /** Список менялся после последнего подключения — нужно переподключиться. */
    val dirty: Boolean = false
)

@HiltViewModel
class BypassDomainsViewModel @Inject constructor(
    private val settingsRepository: SettingsRepository,
    private val subscriptionRepository: SubscriptionRepository,
    @ApplicationContext private val context: Context
) : ViewModel() {

    private val dirty = MutableStateFlow(false)
    private val _error = MutableStateFlow(false)
    val error: StateFlow<Boolean> = _error.asStateFlow()

    val state: StateFlow<BypassDomainsUiState> = combine(
        settingsRepository.bypassDomains, GodjiVpnService.isRunning, dirty
    ) { domains, running, d -> BypassDomainsUiState(domains.sorted(), running, d && running) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), BypassDomainsUiState())

    /** @return true — добавлено (поле ввода можно очистить). */
    fun add(input: String): Boolean {
        val domain = SettingsRepository.normalizeDomain(input)
        if (domain == null) {
            _error.value = true
            return false
        }
        _error.value = false
        viewModelScope.launch { settingsRepository.addBypassDomain(domain) }
        dirty.value = true
        return true
    }

    fun clearError() { _error.value = false }

    fun remove(domain: String) {
        viewModelScope.launch { settingsRepository.removeBypassDomain(domain) }
        dirty.value = true
    }

    /** Правила маршрутизации Xray читаются только при подключении — переподключаемся. */
    fun applyNow() {
        val node = subscriptionRepository.selectedNode() ?: return
        VpnLauncher.connect(context, node)
        dirty.value = false
    }
}

@Composable
fun BypassDomainsScreen(onBack: () -> Unit, viewModel: BypassDomainsViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsState()
    val error by viewModel.error.collectAsState()
    var input by remember { mutableStateOf("") }

    fun submit() {
        if (input.isNotBlank() && viewModel.add(input)) input = ""
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
            Text(Loc.f.bypassTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp)
        }
        Text(
            Loc.f.bypassIntro, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp, lineHeight = 17.sp,
            modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
        )

        // Поле ввода + "Добавить" — капсула как ссылка в "Пригласи друзей".
        Row(
            Modifier
                .fillMaxWidth()
                .height(46.dp)
                .clip(RoundedCornerShape(50))
                .background(GodjiColors.Chip)
                .border(1.dp, if (error) GodjiColors.Danger else GodjiColors.CardBorder, RoundedCornerShape(50))
                .padding(start = 16.dp, end = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
                if (input.isEmpty()) Text(Loc.f.bypassHint, color = GodjiColors.TextSecondary, fontSize = 13.5.sp, maxLines = 1)
                BasicTextField(
                    value = input,
                    onValueChange = { input = it; viewModel.clearError() },
                    singleLine = true,
                    textStyle = TextStyle(color = GodjiColors.TextPrimary, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold),
                    cursorBrush = SolidColor(GodjiColors.Teal),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { submit() }),
                    modifier = Modifier.fillMaxWidth()
                )
            }
            Box(
                Modifier
                    .height(34.dp)
                    .clip(RoundedCornerShape(50))
                    .background(Brush.verticalGradient(0f to GodjiColors.AccentGradTop, 0.55f to GodjiColors.AccentGradMid, 1f to GodjiColors.AccentGradBottom))
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { submit() }
                    .padding(horizontal = 16.dp),
                contentAlignment = Alignment.Center
            ) {
                Text(Loc.f.bypassAdd, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 12.5.sp)
            }
        }
        if (error) {
            Text(Loc.f.bypassInvalid, color = GodjiColors.Danger, fontWeight = FontWeight.SemiBold, fontSize = 11.5.sp, modifier = Modifier.padding(horizontal = 12.dp))
        }

        if (state.dirty) {
            Row(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(20.dp))
                    .background(GodjiColors.TealTint)
                    .padding(start = 14.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(Loc.f.bypassReconnectHint, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.weight(1f))
                Box(
                    Modifier
                        .height(32.dp)
                        .clip(RoundedCornerShape(50))
                        .background(GodjiColors.Thumb)
                        .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
                        .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = viewModel::applyNow)
                        .padding(horizontal = 14.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(Loc.f.bypassApply, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                }
            }
        }

        SectionLabel(Loc.f.bypassListSection(state.domains.size))
        RefCard {
            if (state.domains.isEmpty()) {
                Text(
                    Loc.f.bypassEmpty, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp, lineHeight = 17.sp,
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp)
                )
            }
            state.domains.forEachIndexed { i, domain ->
                if (i > 0) Hair()
                Row(
                    Modifier.fillMaxWidth().padding(start = 14.dp, end = 10.dp, top = 10.dp, bottom = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
                        val shown = runCatching { java.net.IDN.toUnicode(domain) }.getOrDefault(domain)
                        Text(shown, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(Loc.f.bypassWithSubdomains, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.sp)
                    }
                    Box(
                        Modifier.size(28.dp).clip(CircleShape).background(GodjiColors.Hair)
                            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { viewModel.remove(domain) },
                        contentAlignment = Alignment.Center
                    ) {
                        Text("×", color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                }
            }
        }
    }
}
