package xyz.gojihub.vpn

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp
import androidx.core.view.WindowCompat
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import dagger.hilt.android.AndroidEntryPoint
import xyz.gojihub.vpn.auth.AuthRepository
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.connect.ConnectScreen
import xyz.gojihub.vpn.ui.login.LoginScreen
import xyz.gojihub.vpn.ui.login.VerifyEmailScreen
import xyz.gojihub.vpn.ui.navigation.GodjiDestinations
import xyz.gojihub.vpn.ui.plans.PlansScreen
import xyz.gojihub.vpn.ui.servers.ServersScreen
import xyz.gojihub.vpn.ui.settings.AppTunnelingScreen
import xyz.gojihub.vpn.ui.settings.BypassDomainsScreen
import xyz.gojihub.vpn.ui.settings.LogLevelScreen
import xyz.gojihub.vpn.ui.settings.NetworkRulesScreen
import xyz.gojihub.vpn.ui.settings.PingSettingsScreen
import xyz.gojihub.vpn.ui.settings.SettingsScreen
import xyz.gojihub.vpn.ui.support.FaqScreen
import xyz.gojihub.vpn.ui.support.NewTicketScreen
import xyz.gojihub.vpn.ui.support.SupportListScreen
import xyz.gojihub.vpn.ui.support.TicketChatScreen
import xyz.gojihub.vpn.ui.theme.GlassBackdrop
import xyz.gojihub.vpn.ui.theme.GlassTab
import xyz.gojihub.vpn.ui.theme.GlassTabBar
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.GodjiVpnTheme
import xyz.gojihub.vpn.ui.theme.GojiTabIcons
import xyz.gojihub.vpn.ui.theme.ThemeMode
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject lateinit var authRepository: AuthRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        setContent {
            GodjiVpnTheme {
                GodjiApp(startLoggedIn = authRepository.isLoggedIn(), authRepository = authRepository)
            }
        }
    }
}

@Composable
fun GodjiApp(startLoggedIn: Boolean, authRepository: AuthRepository) {
    val systemDark = isSystemInDarkTheme()
    val themeMode = GodjiColors.themeMode
    SideEffect {
        if (themeMode == ThemeMode.SYSTEM && systemDark != GodjiColors.isDark) {
            if (systemDark) GodjiColors.applyDark() else GodjiColors.applyLight()
        }
    }

    // v5: фон GlassBackdrop рисуется и под системными панелями — они прозрачные.
    val view = LocalView.current
    val isDark = GodjiColors.isDark
    SideEffect {
        val window = (view.context as? android.app.Activity)?.window
        if (window != null) {
            @Suppress("DEPRECATION")
            window.statusBarColor = Color.Transparent.toArgb()
            @Suppress("DEPRECATION")
            window.navigationBarColor = Color.Transparent.toArgb()
            val controller = WindowCompat.getInsetsController(window, view)
            controller.isAppearanceLightStatusBars = !isDark
            controller.isAppearanceLightNavigationBars = !isDark
        }
    }

    val navController = rememberNavController()
    val tabs = listOf(
        GlassTab(GodjiDestinations.CONNECT, Loc.s.tabHome, GojiTabIcons.Home),
        GlassTab(GodjiDestinations.SERVERS, Loc.s.tabServers, GojiTabIcons.Servers),
        GlassTab(GodjiDestinations.PLANS, Loc.s.tabPlans, GojiTabIcons.Plans),
        GlassTab(GodjiDestinations.SETTINGS, Loc.s.tabSettings, GojiTabIcons.Settings),
    )
    val backStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = backStackEntry?.destination?.route
    val showBottomBar = currentRoute in tabs.map { it.route }

    val sessionExpired by authRepository.sessionExpired.collectAsState()
    LaunchedEffect(sessionExpired) {
        if (sessionExpired) {
            navController.navigate(GodjiDestinations.LOGIN) {
                popUpTo(0) { inclusive = true }
            }
            authRepository.consumeSessionExpired()
        }
    }

    // Вместо Scaffold + NavigationBar: общий фон → контент → нижняя панель (GlassTabBar).
    GlassBackdrop {
        NavHost(
            navController = navController,
            startDestination = if (startLoggedIn) GodjiDestinations.CONNECT else GodjiDestinations.LOGIN,
            modifier = Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.systemBars)
                // Короткая нижняя панель M3 Expressive — 64dp над системной навигацией.
                .padding(bottom = if (showBottomBar) 64.dp else 0.dp)
        ) {
            composable(GodjiDestinations.LOGIN) {
                LoginScreen(
                    onCodeSent = { email ->
                        navController.navigate(GodjiDestinations.verifyEmail(email))
                    }
                )
            }
            composable(
                route = GodjiDestinations.VERIFY_EMAIL,
                arguments = listOf(navArgument("email") { type = NavType.StringType })
            ) { backStackEntry ->
                val email = backStackEntry.arguments?.getString("email").orEmpty()
                VerifyEmailScreen(
                    email = email,
                    onVerified = {
                        navController.navigate(GodjiDestinations.CONNECT) {
                            popUpTo(GodjiDestinations.LOGIN) { inclusive = true }
                        }
                    }
                )
            }
            composable(
                GodjiDestinations.CONNECT,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                ConnectScreen(
                    onOpenJournal = { navController.navigate(GodjiDestinations.JOURNAL) },
                    onOpenPlans = {
                        navController.navigate(GodjiDestinations.PLANS) {
                            popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                            launchSingleTop = true
                            restoreState = true
                        }
                    },
                    onOpenServers = {
                        navController.navigate(GodjiDestinations.SERVERS) {
                            popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                            launchSingleTop = true
                            restoreState = true
                        }
                    }
                )
            }
            composable(
                GodjiDestinations.SERVERS,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                // Выбор узла сразу возвращает на "Главную" — как pick() в эталоне (screen: 'connect').
                ServersScreen(
                    onServerPicked = {
                        navController.navigate(GodjiDestinations.CONNECT) {
                            popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                            launchSingleTop = true
                            restoreState = true
                        }
                    }
                )
            }
            composable(
                GodjiDestinations.PLANS,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                PlansScreen(onOpenSupport = { navController.navigate(GodjiDestinations.SUPPORT_LIST) })
            }
            composable(
                GodjiDestinations.SETTINGS,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                SettingsScreen(
                    onLoggedOut = {
                        navController.navigate(GodjiDestinations.LOGIN) {
                            popUpTo(0) { inclusive = true }
                        }
                    },
                    onOpenPingSettings = { navController.navigate(GodjiDestinations.PING_SETTINGS) },
                    onOpenLogLevel = { navController.navigate(GodjiDestinations.LOG_LEVEL) },
                    onOpenAppTunneling = { navController.navigate(GodjiDestinations.APP_TUNNELING) },
                    onOpenNetworkRules = { navController.navigate(GodjiDestinations.NETWORK_RULES) },
                    onOpenJournal = { navController.navigate(GodjiDestinations.JOURNAL) },
                    onOpenBypassDomains = { navController.navigate(GodjiDestinations.BYPASS_DOMAINS) },
                    onOpenSupport = { navController.navigate(GodjiDestinations.SUPPORT_LIST) }
                )
            }
            composable(
                GodjiDestinations.SUPPORT_LIST,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                SupportListScreen(
                    onBack = { navController.popBackStack() },
                    onOpenTicket = { ticketId -> navController.navigate(GodjiDestinations.supportTicket(ticketId)) },
                    onNewTicket = { navController.navigate(GodjiDestinations.SUPPORT_NEW) },
                    onOpenFaq = { navController.navigate(GodjiDestinations.SUPPORT_FAQ) }
                )
            }
            composable(
                GodjiDestinations.SUPPORT_NEW,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                NewTicketScreen(
                    onBack = { navController.popBackStack() },
                    onCreated = { ticketId ->
                        navController.navigate(GodjiDestinations.supportTicket(ticketId)) {
                            popUpTo(GodjiDestinations.SUPPORT_LIST) { inclusive = false }
                        }
                    },
                    onGoToTicket = { ticketId ->
                        navController.navigate(GodjiDestinations.supportTicket(ticketId)) {
                            popUpTo(GodjiDestinations.SUPPORT_LIST) { inclusive = false }
                        }
                    }
                )
            }
            composable(
                route = GodjiDestinations.SUPPORT_TICKET,
                arguments = listOf(navArgument("ticketId") { type = NavType.LongType }),
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) { backStackEntry ->
                val ticketId = backStackEntry.arguments?.getLong("ticketId") ?: 0L
                TicketChatScreen(ticketId = ticketId, onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.SUPPORT_FAQ,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                FaqScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.PING_SETTINGS,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                PingSettingsScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.LOG_LEVEL,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                LogLevelScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.BYPASS_DOMAINS,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                BypassDomainsScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.NETWORK_RULES,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                NetworkRulesScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.JOURNAL,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                xyz.gojihub.vpn.ui.settings.JournalScreen(onBack = { navController.popBackStack() })
            }
            composable(
                GodjiDestinations.APP_TUNNELING,
                enterTransition = { EnterTransition.None },
                exitTransition = { ExitTransition.None }
            ) {
                AppTunnelingScreen(onBack = { navController.popBackStack() })
            }
        }

        if (showBottomBar) {
            GlassTabBar(
                tabs = tabs,
                selectedRoute = currentRoute,
                onSelect = { tab ->
                    navController.navigate(tab.route) {
                        popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                        launchSingleTop = true
                        restoreState = true
                    }
                },
                modifier = Modifier.align(Alignment.BottomCenter)
            )
        }
    }
}
