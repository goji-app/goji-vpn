package xyz.gojihub.vpn.ui.support

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.models.FaqItemDto
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton

@Composable
fun FaqScreen(onBack: () -> Unit, viewModel: FaqViewModel = hiltViewModel()) {
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
            Text(Loc.s.support.supportFaqTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 22.sp)
        }

        when {
            state.loading -> Box(Modifier.fillMaxWidth().padding(top = 40.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = GodjiColors.TealDeep)
            }
            state.error -> Box(Modifier.fillMaxWidth().godjiCard().padding(20.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(Loc.s.support.supportLoadError, color = GodjiColors.TextSecondary, fontSize = 12.5.sp)
                    TextButton(onClick = viewModel::load) { Text(Loc.s.support.supportRetry, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold) }
                }
            }
            state.sections.isEmpty() -> Box(Modifier.fillMaxWidth().godjiCard().padding(20.dp), contentAlignment = Alignment.Center) {
                Text(Loc.s.support.supportFaqEmpty, color = GodjiColors.TextSecondary, fontSize = 12.5.sp)
            }
            else -> {
                state.sections.forEach { section ->
                    section.name?.let {
                        Text(it, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp)
                    }
                    Column(
                        Modifier.fillMaxWidth().godjiCard().padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        section.items.forEachIndexed { index, item ->
                            if (index > 0) HorizontalDivider(color = GodjiColors.CardBorder)
                            FaqRow(item)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FaqRow(item: FaqItemDto) {
    var expanded by remember(item.id) { mutableStateOf(false) }
    Column(Modifier.fillMaxWidth().clickable { expanded = !expanded }) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween, modifier = Modifier.fillMaxWidth()) {
            Text(item.question, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 13.sp, modifier = Modifier.weight(1f))
            Text(if (expanded) "−" else "+", color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 16.sp)
        }
        if (expanded) {
            Spacer(Modifier.height(6.dp))
            Text(item.answer, color = GodjiColors.TextSecondary, fontSize = 12.sp, lineHeight = 16.sp)
        }
    }
}
