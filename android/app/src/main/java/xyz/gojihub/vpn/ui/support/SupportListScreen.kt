package xyz.gojihub.vpn.ui.support

import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.ui.util.rememberPressScale

@Composable
fun SupportListScreen(
    onBack: () -> Unit,
    onOpenTicket: (Long) -> Unit,
    onNewTicket: () -> Unit,
    onOpenFaq: () -> Unit,
    viewModel: SupportListViewModel = hiltViewModel()
) {
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
            Text(Loc.s.support.supportTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp)
        }

        val (newInteraction, newScale) = rememberPressScale()
        Row(
            Modifier
                .fillMaxWidth()
                .scale(newScale.value)
                .clip(RoundedCornerShape(50))
                .background(GodjiColors.Ink)
                .clickable(interactionSource = newInteraction, indication = androidx.compose.foundation.LocalIndication.current) { onNewTicket() }
                .padding(vertical = 10.dp),
            horizontalArrangement = Arrangement.Center
        ) {
            Text(Loc.s.support.supportNewTicket, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 13.sp)
        }

        Text(
            Loc.s.support.supportFaqEntry,
            color = GodjiColors.TealDeep,
            fontWeight = FontWeight.Bold,
            fontSize = 12.sp,
            modifier = Modifier.clickable { onOpenFaq() }
        )

        Row(
            Modifier.fillMaxWidth().background(GodjiColors.Chip, RoundedCornerShape(16.dp)).padding(5.dp),
            horizontalArrangement = Arrangement.spacedBy(5.dp)
        ) {
            SupportTab(Loc.s.support.supportTabOpen, selected = state.tab == "open", modifier = Modifier.weight(1f)) { viewModel.selectTab("open") }
            SupportTab(Loc.s.support.supportTabClosed, selected = state.tab == "closed", modifier = Modifier.weight(1f)) { viewModel.selectTab("closed") }
        }

        when {
            state.loading -> Box(Modifier.fillMaxWidth().padding(top = 40.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = GodjiColors.TealDeep)
            }
            state.error -> Box(Modifier.fillMaxWidth().godjiCard().padding(20.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(Loc.s.support.supportLoadError, color = GodjiColors.TextSecondary, fontSize = 12.5.sp)
                    TextButton(onClick = viewModel::refresh) { Text(Loc.s.support.supportRetry, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold) }
                }
            }
            state.tickets.isEmpty() -> Box(Modifier.fillMaxWidth().godjiCard().padding(20.dp), contentAlignment = Alignment.Center) {
                Text(
                    if (state.tab == "open") Loc.s.support.supportEmptyOpen else Loc.s.support.supportEmptyClosed,
                    color = GodjiColors.TextSecondary,
                    fontSize = 12.5.sp
                )
            }
            else -> {
                Column(
                    Modifier.fillMaxWidth().godjiCard().padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    state.tickets.forEachIndexed { index, ticket ->
                        if (index > 0) HorizontalDivider(color = GodjiColors.CardBorder)
                        TicketRow(ticket, onClick = { onOpenTicket(ticket.id) })
                    }
                }
                if (state.canLoadMore) {
                    if (state.loadingMore) {
                        Box(Modifier.fillMaxWidth().padding(vertical = 10.dp), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp, color = GodjiColors.TealDeep)
                        }
                    } else {
                        Text(
                            Loc.s.support.supportLoadMore,
                            color = GodjiColors.TealDeep,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp,
                            modifier = Modifier.fillMaxWidth().clickable { viewModel.loadMore() }.padding(vertical = 6.dp),
                            textAlign = androidx.compose.ui.text.style.TextAlign.Center
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun SupportTab(label: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val bg by animateColorAsState(if (selected) GodjiColors.Ink else androidx.compose.ui.graphics.Color.Transparent, label = "supportTabBg")
    val fg = if (selected) GodjiColors.Surface else GodjiColors.TextSecondary
    Box(
        modifier
            .height(34.dp)
            .clip(RoundedCornerShape(50))
            .background(bg)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Text(label, color = fg, fontWeight = FontWeight.Bold, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
private fun TicketRow(ticket: SupportTicketUi, onClick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onClick),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Column(Modifier.weight(1f)) {
            Text(ticket.title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            ticket.lastMessage?.takeIf { it.isNotBlank() }?.let {
                Text(it, color = GodjiColors.TextSecondary, fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(ticket.statusLabel, color = if (ticket.isClosed) GodjiColors.TextSecondary else GodjiColors.TealDeep, fontWeight = FontWeight.SemiBold, fontSize = 10.sp)
                ticket.dateLabel?.let { Text("· $it", color = GodjiColors.TextSecondary, fontSize = 10.sp) }
            }
        }
        if (ticket.unreadCount > 0) {
            Box(
                Modifier.clip(RoundedCornerShape(50)).background(GodjiColors.Danger).padding(horizontal = 7.dp, vertical = 3.dp)
            ) {
                Text(ticket.unreadCount.toString(), color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 10.sp)
            }
        }
        Icon(
            imageVector = Icons.AutoMirrored.Filled.KeyboardArrowRight,
            contentDescription = null,
            tint = GodjiColors.TextSecondary,
            modifier = Modifier.size(16.dp)
        )
    }
}
