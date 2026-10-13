#!/bin/bash
# Проверка туннеля на macOS (GitHub Actions, sudo без пароля): та же цепочка, что в приложении —
# xray (SOCKS 10808, здесь с выходом freedom вместо VLESS) ← sing-box (TUN, конфиг как в
# VpnEngine.WriteSingBoxConfig) от root. Проверяет, что utun с 172.19.0.1 поднимается, трафик
# Mac идёт через туннель, и снимает журналы в smoke/tunnel-<arch>.log.
# $1 — arm64|x64 (x64 запускается через Rosetta).
set -u
cd "$(dirname "$0")/.."
arch=${1:-arm64}
rt="Runtime/$arch"
run=""; [ "$arch" = x64 ] && run="/usr/bin/arch -x86_64"
work=$(mktemp -d)
mkdir -p smoke
log="smoke/tunnel-$arch.log"
: > "$log"
status=0

cat > "$work/xray.json" <<'EOF'
{"log":{"loglevel":"warning"},
 "inbounds":[{"listen":"127.0.0.1","port":10808,"protocol":"socks","settings":{"udp":true}}],
 "outbounds":[{"protocol":"freedom"}]}
EOF
# Точная копия конфига из VpnEngine.WriteSingBoxConfig.
cat > "$work/sing-box.json" <<'EOF'
{"log":{"level":"info"},
 "dns":{"servers":[{"type":"udp","tag":"dns-remote","server":"1.1.1.1","detour":"socks-out"}],"final":"dns-remote"},
 "inbounds":[{"type":"tun","tag":"tun-in","address":["172.19.0.1/30"],"mtu":1280,"auto_route":true,"strict_route":true,"stack":"system"}],
 "outbounds":[{"type":"socks","tag":"socks-out","server":"127.0.0.1","server_port":10808,"version":"5"},{"type":"direct","tag":"direct"}],
 "route":{"auto_detect_interface":true,
  "rules":[{"action":"sniff"},{"process_name":["xray","GojiVpn"],"outbound":"direct"},
           {"protocol":"dns","action":"hijack-dns"},
           {"ip_cidr":["169.254.0.0/16"],"action":"reject"}],
  "final":"socks-out"}}
EOF

echo "== [$arch] sing-box check" | tee -a "$log"
$run "$rt/sing-box" check -c "$work/sing-box.json" >> "$log" 2>&1 && echo "OK: конфиг принят" || { echo "FAIL: конфиг"; status=1; }

$run "$rt/xray" run -c "$work/xray.json" >> "$log" 2>&1 &
XRAY=$!
sleep 2
ip_before=$(curl -s -m 10 https://api.ipify.org || true)

echo "== [$arch] sing-box run (root)" | tee -a "$log"
sudo $run "$rt/sing-box" run -c "$work/sing-box.json" >> "$log" 2>&1 &
SB=$!
utun=""
for i in $(seq 1 40); do
  utun=$(ifconfig | awk '/^utun[0-9]+:/{n=$1} /inet 172\.19\.0\.1 /{sub(":","",n); print n; exit}')
  [ -n "$utun" ] && break
  sudo kill -0 $SB 2>/dev/null || break
  sleep 0.5
done
if [ -n "$utun" ]; then echo "OK: туннель $utun с 172.19.0.1"; ifconfig "$utun"; else echo "FAIL: utun с 172.19.0.1 не появился"; status=1; fi

# Трафик Mac должен идти через туннель: соединения curl видны в журнале sing-box (inbound tun-in).
sleep 2
code=$(curl -s -o /dev/null -w "%{http_code}" -m 15 https://www.apple.com/ || true)
echo "curl через туннель: HTTP $code"
[ "$code" = 200 ] || status=1
dig +short +time=5 apple.com @8.8.8.8 | head -2 || true
grep -E "inbound/tun\[tun-in\].*(inbound connection|inbound packet connection)" "$log" | head -3
if grep -q "inbound/tun\[tun-in\]" "$log"; then echo "OK: трафик идёт через TUN"; else echo "FAIL: в журнале нет соединений через TUN"; status=1; fi
netstat -rn -f inet | head -12

echo "== [$arch] остановка" | tee -a "$log"
sudo kill $SB 2>/dev/null; sleep 2; sudo kill -9 $SB 2>/dev/null
kill $XRAY 2>/dev/null
sleep 1
ifconfig | grep -q "inet 172.19.0.1 " && { echo "FAIL: utun остался после остановки"; status=1; } || echo "OK: туннель убран"
ip_after=$(curl -s -m 10 https://api.ipify.org || true)
[ -n "$ip_after" ] && echo "OK: сеть после остановки работает" || { echo "FAIL: сеть после остановки"; status=1; }
echo "--- хвост журнала"; tail -n 25 "$log"
exit $status
