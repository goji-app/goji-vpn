import java.security.MessageDigest
import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
    id("com.google.dagger.hilt.android")
    id("com.google.devtools.ksp")
}

// Ключ подписи релиза — файл лежит рядом (app/keystore.properties), не в системе контроля
// версий (проект не под git). Если файла нет (например, свежий чекаут без ключа) — релизная
// сборка просто останется неподписанной, а не упадёт с ошибкой конфигурации.
val keystorePropsFile = rootProject.file("app/keystore.properties")
val keystoreProps = Properties().apply {
    if (keystorePropsFile.exists()) keystorePropsFile.inputStream().use { load(it) }
}

// Адреса кабинета и сайта загрузки в публичный репозиторий не попадают: берутся из переменных
// окружения или из goji-hosts.properties (в корне монорепозитория или рядом с android-проектом,
// в git не хранится). Образец — goji-hosts.example.properties.
val gojiHosts = Properties().apply {
    listOf(rootProject.file("goji-hosts.properties"), rootProject.file("../goji-hosts.properties"))
        .firstOrNull { it.exists() }
        ?.inputStream()?.use { load(it) }
}
fun gojiHost(name: String): String =
    (System.getenv(name) ?: gojiHosts.getProperty(name))?.trim()?.trimEnd('/')?.takeIf { it.startsWith("https://") }
        ?: throw GradleException("Не задан $name: положите goji-hosts.properties в корень репозитория (образец — goji-hosts.example.properties) или задайте переменную окружения")

// Ядро libXray.aar в git не хранится. libs/libXray.version («<версия> <sha256>») — единственный
// источник версии для «О программе», и сборка падает, если рядом лежит другой AAR: 1.0.125
// ушла со старым ядром из папки сборки, а экран показывал новую версию.
val xrayVersion: String = run {
    val (version, sha) = file("libs/libXray.version").readText().trim().split(Regex("\\s+"))
    val aar = file("libs/libXray.aar")
    val actual = MessageDigest.getInstance("SHA-256").digest(aar.readBytes())
        .joinToString("") { "%02x".format(it) }
    if (actual != sha) throw GradleException("libs/libXray.aar не совпадает с libs/libXray.version ($version): $actual")
    version
}

android {
    namespace = "xyz.gojihub.vpn"
    compileSdk = 37

    defaultConfig {
        applicationId = "xyz.gojihub.vpn"
        minSdk = 24 // VpnService + Reality нормально живут с 24+, но проверьте охват вашей аудитории
        targetSdk = 37
        versionCode = 110
        versionName = "1.0.128"

        buildConfigField("String", "SITE_URL", "\"${gojiHost("GOJI_SITE_URL")}\"")
        buildConfigField("String", "DOWNLOAD_URL", "\"${gojiHost("GOJI_DOWNLOAD_URL")}\"")
        buildConfigField("String", "XRAY_VERSION", "\"$xrayVersion\"")

        // libXray.aar тянет нативные .so сразу под 4 ABI — реальные телефоны это почти
        // всегда arm64-v8a (и изредка armeabi-v7a на старых). x86/x86_64 нужны только
        // для эмуляторов и раздувают APK в ~2 раза без всякой пользы для реальных устройств.
        ndk {
            abiFilters += listOf("arm64-v8a", "armeabi-v7a")
        }
    }

    // Две сборки до выхода в Google Play:
    //  • site — APK для сайта загрузки и GitHub: самообновление и кнопка «Оплатить в личном
    //    кабинете» (вне Google Play правила Play не действуют);
    //  • play — AAB для Google Play: ни того, ни другого. Play запрещает обновлять приложение в
    //    обход Play и вести пользователя к оплате цифровой подписки мимо Google Play Billing
    //    (исключения — только программы для США/ЕЭЗ с регистрацией, Россия в них не входит).
    // Манифест site (разрешение на установку APK, приёмник загрузки, FileProvider) —
    // в app/src/site, в play-сборку не попадает. Собирать: assembleSiteRelease, bundlePlayRelease.
    flavorDimensions += "distribution"
    productFlavors {
        create("site") {
            dimension = "distribution"
            buildConfigField("boolean", "ENABLE_SELF_UPDATE", "true")
            buildConfigField("boolean", "ENABLE_CABINET_LINK", "true")
        }
        create("play") {
            dimension = "distribution"
            buildConfigField("boolean", "ENABLE_SELF_UPDATE", "false")
            buildConfigField("boolean", "ENABLE_CABINET_LINK", "false")
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        // minSdk 24 не имеет java.time нативно (появился в API 26) — десахаринг добавляет его.
        isCoreLibraryDesugaringEnabled = true
    }
    signingConfigs {
        if (keystorePropsFile.exists()) {
            create("release") {
                storeFile = file(keystoreProps.getProperty("storeFile"))
                storePassword = keystoreProps.getProperty("storePassword")
                keyAlias = keystoreProps.getProperty("keyAlias")
                keyPassword = keystoreProps.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            if (keystorePropsFile.exists()) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    packaging {
        jniLibs {
            // true = .so сжимаются внутри APK (меньше вес файла для раздачи вручную).
            // Если будете публиковать в Play Store как AAB — можно вернуть false,
            // Play сам решает паковку и предпочитает несжатые/выровненные библиотеки.
            useLegacyPackaging = true
        }
    }

}

// Один universal-APK на сборку (со всеми ABI из ndk.abiFilters выше внутри одного файла) —
// раньше splits.abi дополнительно нарезал его на app-arm64-v8a/app-armeabi-v7a/app-universal;
// от отдельных per-ABI сборок решили отказаться, раздаём и публикуем только universal.
// Имя файла — просто Goji.apk, без версии/архитектуры: версия и так видна внутри самого APK
// (versionName/versionCode) и в имени папки release-X.Y.Z на GitHub.
androidComponents {
    onVariants { variant ->
        variant.outputs.forEach { output -> output.outputFileName.set("Goji.apk") }
    }
}

dependencies {
    // ── Compose ──────────────────────────────────────────
    val composeBom = platform("androidx.compose:compose-bom:2026.08.00")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core")
    // Единый язык иконок (2026-09-28) — заменяет текстовые юникод-глифы (⚡⧉→←✕🏷️📎↗ и т.п.)
    // на векторные Material-иконки по всему приложению. Core-набора не хватает (Bolt/
    // ContentCopy/AttachFile/LocalOffer/OpenInNew там нет) — extended тянет тысячи иконок, но
    // R8 (isMinifyEnabled+isShrinkResources уже включены) вырезает неиспользуемые.
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.navigation:navigation-compose:2.8.3")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.6")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.6")
    implementation("androidx.lifecycle:lifecycle-process:2.8.6") // ProcessLifecycleOwner — обновление при возврате приложения на передний план

    // ── Hilt (DI) ────────────────────────────────────────
    implementation("com.google.dagger:hilt-android:2.60.1")
    ksp("com.google.dagger:hilt-compiler:2.60.1")
    implementation("androidx.hilt:hilt-navigation-compose:1.2.0")

    // ── Сеть: Remnawave API ──────────────────────────────
    implementation("com.squareup.retrofit2:retrofit:2.11.0")
    implementation("com.squareup.retrofit2:converter-moshi:2.11.0")
    implementation("com.squareup.moshi:moshi-kotlin:1.15.1")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")

    // ── Хранение токенов ─────────────────────────────────
    implementation("androidx.security:security-crypto:1.1.0-alpha06")
    implementation("androidx.datastore:datastore-preferences:1.1.1")

    // ── Фоновое обновление подписки/пинга раз в час (работает и когда приложение
    // свёрнуто) — WorkManager с Hilt-инъекцией зависимостей в Worker ──
    implementation("androidx.work:work-runtime-ktx:2.10.0")
    implementation("androidx.hilt:hilt-work:1.2.0")
    ksp("androidx.hilt:hilt-compiler:1.2.0")

    // ── Xray-core / libXray ──────────────────────────────
    // Соберите libbox.aar по инструкции XTLS/libXray (python3 build/main.py android)
    // и положите в app/libs/, либо подключите как maven-артефакт, если решите
    // публиковать свою сборку в приватный репозиторий.
    implementation(files("libs/libXray.aar"))

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.browser:browser:1.8.0") // Custom Tabs для native OAuth
    implementation("io.coil-kt:coil-compose:2.7.0") // Картинки/коллажи/слайд-шоу в новостях (RichContent)
    // QR-коды: генерация (реферальная ссылка, перенос входа на другое устройство) — zxing core;
    // сканирование камерой при входе по QR — zxing-android-embedded (без Google Play Services,
    // работает и в сборке "direct" на устройствах без GMS).
    implementation("com.google.zxing:core:3.5.3")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.3")

    testImplementation("junit:junit:4.13.2")
}
