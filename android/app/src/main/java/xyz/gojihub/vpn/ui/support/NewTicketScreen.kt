package xyz.gojihub.vpn.ui.support

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.models.SupportQueueDto
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.ui.util.rememberPressScale

@Composable
fun NewTicketScreen(
    onBack: () -> Unit,
    onCreated: (Long) -> Unit,
    onGoToTicket: (Long) -> Unit,
    viewModel: NewTicketViewModel = hiltViewModel()
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
            Text(Loc.s.support.supportNewTicketTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 22.sp)
        }

        if (state.limitReached) {
            Column(
                Modifier.fillMaxWidth().godjiCard(tint = GodjiColors.TerracottaTint, borderColor = GodjiColors.TerracottaTintBorder).padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(Loc.s.support.supportNewTicketLimitTitle, color = GodjiColors.TerracottaDeep, fontWeight = FontWeight.Bold, fontSize = 14.sp)
                Text(Loc.s.support.supportNewTicketLimitText, color = GodjiColors.TextSecondary, fontSize = 12.sp)
                state.activeTicketId?.let { ticketId ->
                    val (goInteraction, goScale) = rememberPressScale()
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .scale(goScale.value)
                            .clip(RoundedCornerShape(50))
                            .background(GodjiColors.Ink)
                            .clickable(interactionSource = goInteraction, indication = androidx.compose.foundation.LocalIndication.current) { onGoToTicket(ticketId) }
                            .padding(vertical = 9.dp),
                        horizontalArrangement = Arrangement.Center
                    ) {
                        Text(Loc.s.support.supportNewTicketLimitGoTo, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 12.5.sp)
                    }
                }
            }
            return@Column
        }

        if (state.queues.size > 1) {
            Text(Loc.s.support.supportNewTicketQueueLabel, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 11.sp)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(7.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                state.queues.forEach { queue ->
                    QueueChip(queue, selected = state.selectedQueueId == queue.id, onSelect = { viewModel.selectQueue(queue.id) })
                }
            }
        }

        OutlinedTextField(
            value = state.subject,
            onValueChange = viewModel::setSubject,
            label = { Text(Loc.s.support.supportNewTicketSubjectLabel) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )

        OutlinedTextField(
            value = state.message,
            onValueChange = viewModel::setMessage,
            placeholder = { Text(Loc.s.support.supportNewTicketMessagePlaceholder) },
            minLines = 5,
            modifier = Modifier.fillMaxWidth()
        )

        state.error?.let {
            Text(it, color = GodjiColors.Danger, fontWeight = FontWeight.SemiBold, fontSize = 11.5.sp)
        }

        val (submitInteraction, submitScale) = rememberPressScale()
        Row(
            Modifier
                .fillMaxWidth()
                .scale(submitScale.value)
                .clip(RoundedCornerShape(50))
                .background(GodjiColors.Ink)
                .clickable(interactionSource = submitInteraction, indication = androidx.compose.foundation.LocalIndication.current, enabled = !state.submitting) {
                    viewModel.submit(onCreated)
                }
                .padding(vertical = 14.dp),
            horizontalArrangement = Arrangement.Center
        ) {
            if (state.submitting) {
                CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = GodjiColors.Surface)
            } else {
                Text(Loc.s.support.supportNewTicketSubmit, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 13.sp)
            }
        }
    }
}

@Composable
private fun QueueChip(queue: SupportQueueDto, selected: Boolean, onSelect: () -> Unit) {
    val bg = if (selected) GodjiColors.Ink else GodjiColors.Chip
    val fg = if (selected) GodjiColors.Surface else GodjiColors.TextPrimary
    Box(
        Modifier
            .clip(RoundedCornerShape(50))
            .background(bg)
            .clickable(onClick = onSelect)
            .padding(horizontal = 14.dp, vertical = 8.dp)
    ) {
        Text(queue.name, color = fg, fontWeight = FontWeight.SemiBold, fontSize = 12.sp)
    }
}
