package xyz.gojihub.vpn.widget

import android.content.Context
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository

/** Виджет и его receiver'ы не Hilt-компоненты (ни Activity, ни Service, ни Fragment) —
 *  единственный официальный способ достать Hilt-синглтон оттуда, не плодя второй экземпляр
 *  SubscriptionRepository (со своим отдельным состоянием, не совпадающим с тем, что видит
 *  остальное приложение). */
@EntryPoint
@InstallIn(SingletonComponent::class)
interface WidgetEntryPoint {
    fun subscriptionRepository(): SubscriptionRepository
    fun settingsRepository(): SettingsRepository
    fun pingRepository(): xyz.gojihub.vpn.subscription.PingRepository
}

fun Context.widgetEntryPoint(): WidgetEntryPoint =
    EntryPointAccessors.fromApplication(applicationContext, WidgetEntryPoint::class.java)
