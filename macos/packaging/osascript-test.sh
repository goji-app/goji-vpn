#!/bin/bash
# Запуск root-обёртки ровно так, как это делает приложение (VpnEngine.StartPrivilegedTunnelAsync):
# osascript "with timeout … do shell script … with administrator privileges … end timeout",
# обёртка на переднем плане, osascript живёт, пока жив туннель. В CI нет окна пароля, поэтому
# на одноразовой машине заводится админ с известным паролем. Туннель держится 150 с — дольше
# стандартного тайм-аута Apple Event (120 с), затем останавливается файлом-сигналом.
set -u
cd "$(dirname "$0")/.."
arch=${1:-arm64}
rt="$PWD/Runtime/$arch"
work=$(mktemp -d)
mkdir -p smoke "$work/state"
status=0
sblog="$work/sing-box.log"; wlog="$work/tun-wrapper.log"; stop="$work/state/tun.stop"; pid="$work/state/tun.pid"
PASS="GojiCi-$RANDOM$RANDOM"
ADMIN=gojici
sudo sysadminctl -addUser "$ADMIN" -password "$PASS" -admin 2>&1 | tail -1

sed -n '/const string script = """/,/""";/p' GojiVpn.Mac/Services/VpnEngine.cs | sed '1d;$d' | sed 's/^            //' > "$work/wrapper.sh"
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

# Те же ShQuote/AppleScriptEscape и та же команда, что в VpnEngine.cs.
ADMIN="$ADMIN" python3 - "$work" "$rt/sing-box" "$sblog" "$$" "$stop" "$pid" "$PASS" "$wlog" > "$work/apple.txt" <<'PY'
import os, sys
work, sb, log, app_pid, stop, pid, password, wlog = sys.argv[1:]
q = lambda s: "'" + s.replace("'", "'\\''") + "'"
esc = lambda s: s.replace("\\", "\\\\").replace('"', '\\"')
script = open(f"{work}/wrapper.sh").read().replace("\r", "")
shell = ("/bin/sh -c " + q(script) + " goji-tun " + q(sb) + " " + q(f"{work}/sing-box.json") + " " + q(log) +
         " " + app_pid + " " + q(stop) + " " + q(pid) + " 1.1.1.1 >> " + q(wlog) + " 2>&1")
print(f'do shell script "{esc(shell)}" with administrator privileges user name "{os.environ["ADMIN"]}" password "{password}"')
PY

dotnet build -v q -nologo packaging/tun-probe -o "$work/probe" > /dev/null || { echo "FAIL: сборка tun-probe"; exit 1; }
"$rt/xray" run -c "$work/xray.json" > "$work/xray.log" 2>&1 &
XRAY=$!
sleep 2

echo "== [$arch] osascript … with administrator privileges (на переднем плане, как в приложении)"
osascript -e "with timeout of 31536000 seconds" -e "$(cat "$work/apple.txt")" -e "end timeout" > "$work/osa.out" 2>&1 &
OSA=$!
for i in $(seq 1 60); do [ -f "$pid" ] && break; kill -0 $OSA 2>/dev/null || break; sleep 0.5; done
[ -f "$pid" ] && echo "OK: pid-файл $(cat "$pid")" || { echo "FAIL: pid-файл не появился"; cat "$work/osa.out"; status=1; }
if dotnet "$work/probe/tun-probe.dll" 40; then :; else status=1; fi
code=$(curl -s -o /dev/null -w "%{http_code}" -m 15 https://www.apple.com/ || true)
echo "curl: HTTP $code"

echo "== держим туннель 150 с (дольше тайм-аута Apple Event)"
sleep 150
kill -0 $OSA 2>/dev/null && echo "OK: osascript жив" || { echo "FAIL: osascript завершился: $(cat "$work/osa.out")"; status=1; }
ifconfig | grep -q "inet 172.19.0.1 " && echo "OK: туннель держится" || { echo "FAIL: туннель пропал"; status=1; }
echo "DNS при VPN: $(networksetup -getdnsservers Ethernet | tr '\n' ' ')"
code=$(curl -s -o /dev/null -w "%{http_code}" -m 20 https://www.apple.com/ || true)
[ "$code" = 200 ] && echo "OK: сайты открываются при подменённом DNS (HTTP $code)" || { echo "FAIL: сайты не открываются при подменённом DNS (HTTP $code)"; status=1; }
grep -c "hijack\|inbound DNS packet" "$sblog" | sed 's/^/DNS-пакетов в туннеле: /'

echo "== остановка файлом-сигналом"
touch "$stop"
for i in $(seq 1 40); do kill -0 $OSA 2>/dev/null || break; sleep 0.5; done
if kill -0 $OSA 2>/dev/null; then echo "FAIL: osascript не завершился"; status=1; else wait $OSA; echo "OK: osascript завершился (код $?)"; fi
[ -f "$pid" ] && { echo "FAIL: pid-файл остался"; status=1; } || echo "OK: pid-файл удалён"
[ -f "$stop" ] && { echo "FAIL: файл-сигнал остался"; status=1; } || echo "OK: файл-сигнал удалён"
ifconfig | grep -q "inet 172.19.0.1 " && { echo "FAIL: utun остался"; status=1; } || echo "OK: туннель убран"
kill $XRAY 2>/dev/null
cp "$sblog" "smoke/osascript-$arch-sing-box.log" 2>/dev/null; cp "$wlog" "smoke/osascript-$arch-wrapper.log" 2>/dev/null
echo "--- журнал обёртки"; cat "$wlog" 2>/dev/null || echo "(нет)"
exit $status
