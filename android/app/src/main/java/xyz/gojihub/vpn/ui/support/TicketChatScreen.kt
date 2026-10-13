package xyz.gojihub.vpn.ui.support

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.Close
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
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.ui.util.rememberPressScale
import xyz.gojihub.vpn.util.LogCategory

@Composable
fun TicketChatScreen(
    ticketId: Long,
    onBack: () -> Unit,
    viewModel: TicketChatViewModel = hiltViewModel()
) {
    val state by viewModel.state.collectAsState()
    val listState = rememberLazyListState()
    var showAttachMenu by remember { mutableStateOf(false) }
    var showLogPicker by remember { mutableStateOf(false) }

    val mediaLauncher = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia()) { uris ->
        viewModel.addFileAttachments(uris)
    }
    val fileLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris: List<Uri> ->
        viewModel.addFileAttachments(uris)
    }

    if (showLogPicker) {
        LogPickerDialog(
            onDismiss = { showLogPicker = false },
            onPick = { category, label ->
                viewModel.addLogAttachment(category, label)
                showLogPicker = false
            }
        )
    }

    LaunchedEffect(ticketId) { viewModel.start(ticketId) }
    LaunchedEffect(state.messages.size) {
        if (state.messages.isNotEmpty()) listState.animateScrollToItem(state.messages.size - 1)
    }

    Column(Modifier.fillMaxSize().background(GodjiColors.Background)) {
        Row(
            Modifier.fillMaxWidth().padding(18.dp, 18.dp, 18.dp, 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            BackButton(onClick = onBack)
            Text(
                state.ticketTitle.ifBlank { Loc.s.support.supportTitle },
                color = GodjiColors.TextPrimary,
                fontFamily = SpaceGroteskFamily,
                fontWeight = FontWeight.SemiBold,
                fontSize = 19.sp,
                maxLines = 1,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f)
            )
        }

        when {
            state.loading -> Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = GodjiColors.TealDeep)
            }
            state.loadError -> Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(Loc.s.support.supportChatLoadError, color = GodjiColors.TextSecondary, fontSize = 12.5.sp)
                    TextButton(onClick = viewModel::refresh) { Text(Loc.s.support.supportRetry, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold) }
                }
            }
            else -> {
                LazyColumn(
                    state = listState,
                    modifier = Modifier.weight(1f).fillMaxWidth().padding(horizontal = 18.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                    contentPadding = PaddingValues(vertical = 8.dp)
                ) {
                    itemsIndexed(state.messages, key = { _, m -> m.id }) { _, message ->
                        MessageBubble(message)
                    }
                }
            }
        }

        if (state.isClosed) {
            Text(
                Loc.s.support.supportChatClosedNotice,
                color = GodjiColors.TextSecondary,
                fontWeight = FontWeight.SemiBold,
                fontSize = 11.5.sp,
                modifier = Modifier.fillMaxWidth().padding(18.dp, 0.dp, 18.dp, 14.dp),
                textAlign = androidx.compose.ui.text.style.TextAlign.Center
            )
        } else {
            Column(Modifier.fillMaxWidth().padding(18.dp, 8.dp, 18.dp, 14.dp)) {
                if (state.sendError) {
                    Text(Loc.s.support.supportChatSendError, color = GodjiColors.Danger, fontSize = 11.sp, modifier = Modifier.padding(bottom = 6.dp))
                }
                if (state.attachments.isNotEmpty()) {
                    LazyRow(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.padding(bottom = 6.dp)) {
                        items(state.attachments) { attachment ->
                            AttachmentChip(attachment.name, onRemove = { viewModel.removeAttachment(attachment) })
                        }
                    }
                }
                Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Box {
                        val (attachInteraction, attachScale) = rememberPressScale()
                        Box(
                            Modifier
                                .size(48.dp)
                                .scale(attachScale.value)
                                .clip(RoundedCornerShape(50))
                                .background(GodjiColors.Chip)
                                .clickable(interactionSource = attachInteraction, indication = androidx.compose.foundation.LocalIndication.current) { showAttachMenu = true },
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(Icons.Filled.AttachFile, contentDescription = null, tint = GodjiColors.TextPrimary, modifier = Modifier.size(20.dp))
                        }
                        DropdownMenu(expanded = showAttachMenu, onDismissRequest = { showAttachMenu = false }) {
                            DropdownMenuItem(
                                text = { Text(Loc.s.support.supportAttachMedia) },
                                onClick = {
                                    showAttachMenu = false
                                    mediaLauncher.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo))
                                }
                            )
                            DropdownMenuItem(
                                text = { Text(Loc.s.support.supportAttachFile) },
                                onClick = {
                                    showAttachMenu = false
                                    // Только PDF — фото/видео уже отдельным пунктом меню выше, а
                                    // бэкенд принимает вложениями исключительно image/*, video/*
                                    // и application/pdf (см. комментарий у PendingAttachment в
                                    // TicketChatViewModel) и отвечает HTTP 415 на что угодно ещё.
                                    fileLauncher.launch(arrayOf("application/pdf"))
                                }
                            )
                            DropdownMenuItem(
                                text = { Text(Loc.s.support.supportAttachLogs) },
                                onClick = {
                                    showAttachMenu = false
                                    showLogPicker = true
                                }
                            )
                        }
                    }
                    OutlinedTextField(
                        value = state.input,
                        onValueChange = viewModel::setInput,
                        placeholder = { Text(Loc.s.support.supportChatInputPlaceholder) },
                        modifier = Modifier.weight(1f),
                        maxLines = 4
                    )
                    val (sendInteraction, sendScale) = rememberPressScale()
                    Box(
                        Modifier
                            .size(48.dp)
                            .scale(sendScale.value)
                            .clip(RoundedCornerShape(50))
                            .background(GodjiColors.Ink)
                            .clickable(
                                interactionSource = sendInteraction,
                                indication = androidx.compose.foundation.LocalIndication.current,
                                enabled = !state.sending && (state.input.isNotBlank() || state.attachments.isNotEmpty()),
                                onClick = viewModel::send
                            ),
                        contentAlignment = Alignment.Center
                    ) {
                        if (state.sending) {
                            CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = GodjiColors.Surface)
                        } else {
                            Icon(Icons.AutoMirrored.Filled.Send, contentDescription = null, tint = GodjiColors.Surface, modifier = Modifier.size(18.dp))
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun AttachmentChip(name: String, onRemove: () -> Unit) {
    Row(
        Modifier
            .clip(RoundedCornerShape(50))
            .background(GodjiColors.Chip)
            .padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Text(name, color = GodjiColors.TextPrimary, fontSize = 10.5.sp, maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis, modifier = Modifier.widthIn(max = 120.dp))
        Icon(
            Icons.Filled.Close, contentDescription = null, tint = GodjiColors.TextSecondary,
            modifier = Modifier.size(13.dp).clickable(onClick = onRemove)
        )
    }
}

@Composable
private fun LogPickerDialog(onDismiss: () -> Unit, onPick: (LogCategory, String) -> Unit) {
    val options = listOf(
        LogCategory.MAIN to Loc.s.logMain,
        LogCategory.CORE to Loc.s.logCore,
        LogCategory.SUBSCRIPTION to Loc.s.logSubscription,
        LogCategory.SERVICE to Loc.s.logService,
        LogCategory.PUSH to Loc.s.logPush
    )
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(Loc.s.support.supportAttachLogs) },
        text = {
            Column {
                options.forEach { (category, label) ->
                    Text(
                        label,
                        color = GodjiColors.TextPrimary,
                        fontWeight = FontWeight.Medium,
                        fontSize = 13.sp,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onPick(category, label) }
                            .padding(vertical = 10.dp)
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(Loc.s.plansDevicesCancel) }
        }
    )
}

@Composable
private fun MessageBubble(message: SupportMessageUi) {
    if (message.isEvent) {
        Text(
            message.text ?: "",
            color = GodjiColors.TextSecondary,
            fontSize = 10.5.sp,
            fontWeight = FontWeight.Medium,
            modifier = Modifier.fillMaxWidth(),
            textAlign = androidx.compose.ui.text.style.TextAlign.Center
        )
        return
    }
    val bg = if (message.isMine) GodjiColors.Ink else GodjiColors.Surface
    val fg = if (message.isMine) GodjiColors.Surface else GodjiColors.TextPrimary
    Row(Modifier.fillMaxWidth(), horizontalArrangement = if (message.isMine) Arrangement.End else Arrangement.Start) {
        Column(
            Modifier
                .fillMaxWidth(0.8f)
                .clip(RoundedCornerShape(16.dp))
                .background(bg)
                .padding(12.dp)
        ) {
            if (!message.isMine && !message.senderName.isNullOrBlank()) {
                Text(message.senderName, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 10.5.sp)
                Spacer(Modifier.height(2.dp))
            }
            message.text?.takeIf { it.isNotBlank() }?.let {
                Text(it, color = fg, fontSize = 13.sp, lineHeight = 17.sp)
            }
            message.attachmentNames.forEach { name ->
                Text(Loc.s.support.supportChatAttachment(name), color = fg.copy(alpha = 0.85f), fontSize = 11.5.sp, modifier = Modifier.padding(top = 4.dp))
            }
            message.timeLabel?.let {
                Spacer(Modifier.height(4.dp))
                Text(it, color = fg.copy(alpha = 0.6f), fontSize = 9.5.sp)
            }
        }
    }
}
