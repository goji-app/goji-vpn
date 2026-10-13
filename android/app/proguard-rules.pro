# Правила минификации добавляются по мере надобности —
# особенно после подключения libXray (gomobile) и Moshi-реflection адаптеров.

# Moshi (moshi-kotlin, reflection-based KotlinJsonAdapterFactory) разбирает data class'ы
# из xyz.gojihub.vpn.network.models через рефлексию по именам конструктора/полей в рантайме —
# R8 может переименовать/выкинуть эти классы как "неиспользуемые напрямую", что молча сломает
# парсинг ответов бэкенда только в релизной (минифицированной) сборке. Держим модели как есть.
-keep class xyz.gojihub.vpn.network.models.** { *; }
-keepclassmembers class xyz.gojihub.vpn.network.models.** { *; }
-keep @com.squareup.moshi.JsonQualifier interface *
-keepattributes *Annotation*, Signature, InnerClasses, EnclosingMethod
-keep class kotlin.Metadata { *; }

# androidx.security:security-crypto (Google Tink) ссылается на compile-only аннотации
# errorprone, которых нет в рантайм-classpath — R8 иначе падает с "Missing class" на этапе
# minifyReleaseWithR8. Сами аннотации ни на что не влияют в байткоде, безопасно игнорировать.
-dontwarn com.google.errorprone.annotations.**

# WorkManager хранит очередь фонового обновления подписки в Room (WorkDatabase) и создаёт
# сгенерированный WorkDatabase_Impl рефлективно (Class.forName + пустой конструктор) —
# без явного keep-правила R8 стриппует/переименовывает этот конструктор, и приложение падает
# на самом старте (проверено живьём: NoSuchMethodException: WorkDatabase_Impl.<init>).
-keep class * extends androidx.room.RoomDatabase { <init>(); }
-keep class androidx.work.impl.** { *; }

# hev.htproxy.TProxyService — пакет/имя класса и сигнатуры native-методов зашиты в самой
# нативной библиотеке (hev-jni.c делает FindClass("hev/htproxy/TProxyService") +
# RegisterNatives по точным дескрипторам). Стандартное дефолтное правило для native-методов
# на практике не уберегло класс: живьём поймали "UnsatisfiedLinkError: JNI_ERR returned from
# JNI_OnLoad" именно в релизной (минифицированной) сборке — R8 что-то в классе всё равно менял.
# Держим класс целиком без каких-либо изменений.
-keep class hev.htproxy.** { *; }
