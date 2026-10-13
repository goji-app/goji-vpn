#!/bin/bash
# Проверка root-обёртки туннеля из VpnEngine.cs (её текст вырезается прямо из исходника) так,
# как её запускает приложение: от root, с окружением как у "do shell script" (вместо окна пароля
# osascript здесь sudo). Готовность ловит tun-probe — тем же кодом .NET, что в приложении.
# Затем проверяет подмену DNS, трафик, остановку по файлу-сигналу и восстановление DNS.
# $1 — arm64|x64 (бинарник x64 на Apple Silicon сам уходит в Rosetta).
set -u
cd "$(dirname "$0")/.."
arch=${1:-arm64}
rt="$PWD/Runtime/$arch"
work=$(mktemp -d)
mkdir -p smoke "$work/state"
status=0
sblog="$work/sing-box.log"; stop="$work/state/tun.stop"; pid="$work/state/tun.pid"

sed -n '/const string script = """/,/""";/p' GojiVpn.Mac/Services/VpnEngine.cs | sed '1d;$d' | sed 's/^            //' > "$work/wrapper.sh"
echo "== [$arch] обёртка из VpnEngine.cs: $(wc -l < "$work/wrapper.sh") строк"

cat > "$work/xray.json" <<'EOF'
{"log":{"loglevel":"warning"},
 "inbounds":[{"listen":"127.0.0.1","port":10808,"protocol":"socks","settings":{"udp":true}}],
 "outbounds":[{"protocol":"freedom"}]}
EOF
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

dns_state() { networksetup -listallnetworkservices | tail -n +2 | grep -v '^\*' | while IFS= read -r s; do printf '%s=%s; ' "$s" "$(networksetup -getdnsservers "$s" | tr '\n' ' ')"; done; }
dns_before=$(dns_state)
echo "DNS до: $dns_before"

dotnet build -v q -nologo packaging/tun-probe -o "$work/probe" > /dev/null || { echo "FAIL: сборка tun-probe"; exit 1; }
"$rt/xray" run -c "$work/xray.json" > "$work/xray.log" 2>&1 &
XRAY=$!
sleep 2

echo "== [$arch] запуск обёртки от root"
sudo env -i PATH=/usr/bin:/bin:/usr/sbin:/sbin HOME=/var/root \
  nohup /bin/sh -c "$(cat "$work/wrapper.sh")" goji-tun "$rt/sing-box" "$work/sing-box.json" "$sblog" $$ "$stop" "$pid" 1.1.1.1 \
  > /dev/null 2>&1 &
if dotnet "$work/probe/tun-probe.dll" 40; then :; else status=1; fi
[ -f "$pid" ] && echo "OK: pid-файл $(cat "$pid")" || { echo "FAIL: нет pid-файла"; status=1; }
sleep 3
echo "DNS при VPN: $(dns_state)"
dns_state | grep -q "1.1.1.1" && echo "OK: DNS подменён на 1.1.1.1" || { echo "FAIL: DNS не подменён"; status=1; }
code=$(curl -s -o /dev/null -w "%{http_code}" -m 15 https://www.apple.com/ || true)
echo "curl: HTTP $code"; [ "$code" = 200 ] || status=1
grep -q "inbound/tun\[tun-in\]" "$sblog" && echo "OK: трафик через TUN" || { echo "FAIL: нет трафика через TUN"; status=1; }

echo "== [$arch] остановка по файлу-сигналу"
touch "$stop"
for i in $(seq 1 30); do [ -f "$pid" ] || break; sleep 0.5; done
[ -f "$pid" ] && { echo "FAIL: обёртка не завершилась"; status=1; } || echo "OK: обёртка завершилась"
sleep 2
ifconfig | grep -q "inet 172.19.0.1 " && { echo "FAIL: utun остался"; status=1; } || echo "OK: туннель убран"
dns_after=$(dns_state)
echo "DNS после: $dns_after"
[ "$dns_after" = "$dns_before" ] && echo "OK: DNS восстановлен" || { echo "FAIL: DNS не восстановлен"; status=1; }
kill $XRAY 2>/dev/null
cp "$sblog" "smoke/wrapper-$arch-sing-box.log" 2>/dev/null
echo "--- хвост журнала sing-box"; tail -n 15 "$sblog" 2>/dev/null
exit $status
