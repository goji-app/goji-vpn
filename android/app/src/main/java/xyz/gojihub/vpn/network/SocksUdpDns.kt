package xyz.gojihub.vpn.network

import java.io.DataInputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import kotlin.random.Random

/**
 * Минимальный DNS-запрос (тип A) по UDP через SOCKS5 UDP ASSOCIATE локального Xray —
 * так же, как DNS-пакеты обычных приложений из tun попадают в Xray через hev-socks5-tunnel.
 * Нужен только проверке утечек (NetworkDiagnostics), поэтому без кэшей и повторов.
 */
object SocksUdpDns {

    fun resolveA(name: String, dnsServer: String, socksPort: Int, timeoutMs: Int = 6000): String? {
        Socket().use { control ->
            control.soTimeout = timeoutMs
            control.connect(InetSocketAddress("127.0.0.1", socksPort), timeoutMs)
            val out = control.getOutputStream()
            val input = DataInputStream(control.getInputStream())
            // Приветствие: версия 5, один метод — без аутентификации.
            out.write(byteArrayOf(5, 1, 0)); out.flush()
            val greet = ByteArray(2).also { input.readFully(it) }
            if (greet[0] != 5.toByte() || greet[1] != 0.toByte()) return null
            // UDP ASSOCIATE, адрес клиента 0.0.0.0:0.
            out.write(byteArrayOf(5, 3, 0, 1, 0, 0, 0, 0, 0, 0)); out.flush()
            val head = ByteArray(4).also { input.readFully(it) }
            if (head[1] != 0.toByte()) return null
            val relayHost: InetAddress = when (head[3].toInt()) {
                1 -> InetAddress.getByAddress(ByteArray(4).also { input.readFully(it) })
                4 -> InetAddress.getByAddress(ByteArray(16).also { input.readFully(it) })
                3 -> {
                    val len = input.readUnsignedByte()
                    InetAddress.getByName(String(ByteArray(len).also { input.readFully(it) }))
                }
                else -> return null
            }
            val relayPort = input.readUnsignedShort()
            // Xray может ответить 0.0.0.0 — тогда реле на том же адресе, что и TCP-контроль.
            val relay = if (relayHost.isAnyLocalAddress) InetAddress.getByName("127.0.0.1") else relayHost

            val id = Random.nextInt(0, 0xFFFF)
            val query = buildQuery(id, name)
            val server = InetAddress.getByName(dnsServer).address
            val packet = ByteBuffer.allocate(10 + query.size)
                .put(byteArrayOf(0, 0, 0, 1)).put(server).putShort(53)
                .put(query).array()

            DatagramSocket().use { udp ->
                udp.soTimeout = timeoutMs
                udp.send(DatagramPacket(packet, packet.size, relay, relayPort))
                val buf = ByteArray(1500)
                val reply = DatagramPacket(buf, buf.size)
                udp.receive(reply)
                // Заголовок SOCKS UDP: RSV(2) FRAG(1) ATYP(1) ADDR PORT(2).
                val offset = when (buf[3].toInt()) {
                    1 -> 10
                    4 -> 22
                    3 -> 7 + (buf[4].toInt() and 0xFF)
                    else -> return null
                }
                return parseFirstA(buf, offset, reply.length, id)
            }
        }
    }

    private fun buildQuery(id: Int, name: String): ByteArray {
        val labels = name.trimEnd('.').split('.')
        val buf = ByteBuffer.allocate(12 + labels.sumOf { it.length + 1 } + 1 + 4)
        buf.putShort(id.toShort()).putShort(0x0100).putShort(1).putShort(0).putShort(0).putShort(0)
        labels.forEach { label -> buf.put(label.length.toByte()).put(label.toByteArray()) }
        buf.put(0).putShort(1).putShort(1)
        return buf.array()
    }

    private fun parseFirstA(buf: ByteArray, start: Int, end: Int, id: Int): String? {
        val msg = ByteBuffer.wrap(buf, start, end - start).slice()
        if ((msg.short.toInt() and 0xFFFF) != id) return null
        msg.short // flags
        val qd = msg.short.toInt() and 0xFFFF
        val an = msg.short.toInt() and 0xFFFF
        msg.short; msg.short
        repeat(qd) { skipName(msg); msg.short; msg.short }
        repeat(an) {
            skipName(msg)
            val type = msg.short.toInt() and 0xFFFF
            msg.short // class
            msg.int // ttl
            val len = msg.short.toInt() and 0xFFFF
            if (type == 1 && len == 4) {
                val a = ByteArray(4).also { msg.get(it) }
                return InetAddress.getByAddress(a).hostAddress
            }
            msg.position(msg.position() + len)
        }
        return null
    }

    private fun skipName(msg: ByteBuffer) {
        while (true) {
            val len = msg.get().toInt() and 0xFF
            if (len == 0) return
            if (len and 0xC0 == 0xC0) { msg.get(); return }
            msg.position(msg.position() + len)
        }
    }
}
