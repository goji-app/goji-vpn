package hev.htproxy

/**
 * Пакет и имя класса ("hev/htproxy/TProxyService") зафиксированы в самой нативной библиотеке
 * (hev-jni.c: PKGNAME=hev/htproxy, CLSNAME=TProxyService, JNI_OnLoad делает FindClass именно
 * по этому пути и RegisterNatives) — их нельзя поменять без пересборки libhev-socks5-tunnel.so
 * из исходников, поэтому этот файл обязан жить ровно в этом пакете.
 *
 * hev-socks5-tunnel — отдельный проверенный tun2socks (используется во многих реальных
 * Android VPN-клиентах): читает пакеты из нашего tun-дескриптора и сам пробрасывает их в
 * локальный SOCKS5, который поднимает Xray. Понадобился взамен встроенного tun-инбаунда
 * Xray-core, потому что передача fd в тот через переменную окружения xray.tun.fd не работает
 * в используемой сборке libXray (см. комментарии в GodjiVpnService) — здесь же fd передаётся
 * напрямую как обычный параметр JNI-вызова, а не через окружение процесса.
 */
object TProxyService {
    init {
        System.loadLibrary("hev-socks5-tunnel")
    }

    @JvmStatic external fun TProxyStartService(configPath: String, fd: Int): Boolean
    @JvmStatic external fun TProxyStopService(): Boolean
    @JvmStatic external fun TProxyIsRunning(): Boolean
    @JvmStatic external fun TProxyGetStats(): LongArray
}
