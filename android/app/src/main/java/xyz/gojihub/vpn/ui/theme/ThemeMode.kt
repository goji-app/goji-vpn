package xyz.gojihub.vpn.ui.theme

import android.content.Context
import android.content.res.Configuration

/** Светлая/тёмная — явный выбор пользователя; Системная — приложение следует текущей теме
 *  Android (и живо реагирует на её смену, см. GodjiApp.kt) вместо того чтобы всегда открываться
 *  светлым, как было раньше ("Системную тёмную тему устройства мы всегда игнорировали"). */
enum class ThemeMode { LIGHT, DARK, SYSTEM }

/** Синхронная проверка вне Compose (загрузка приложения, ViewModel) — то же самое, что
 *  isSystemInDarkTheme() внутри @Composable, но доступно и в GodjiApplication.onCreate(). */
fun isSystemInDarkMode(context: Context): Boolean =
    context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
