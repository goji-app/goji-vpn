Сюда кладутся бинарники ядра (не входят в исходники, скачиваются отдельно):

  xray.exe      — XTLS/Xray-core (https://github.com/XTLS/Xray-core/releases),
                  релиз Xray-windows-64.zip. Отвечает только за протокол (VLESS+XHTTP+
                  Reality) к бэкенду, отдаёт локальный SOCKS5 на 127.0.0.1:10808.
  sing-box.exe  — SagerNet/sing-box (https://github.com/SagerNet/sing-box/releases),
                  релиз sing-box-<version>-windows-amd64.zip. Поднимает системный
                  TUN-адаптер (Wintun) и форвардит весь трафик на локальный SOCKS xray.exe.
  wintun.dll    — wintun.net (WireGuard) — нужен sing-box.exe для TUN-адаптера, рядом с ним.
  geoip.dat     — Loyalsoldier/v2ray-rules-dat, для routing-правил по гео-базам.
  geosite.dat   — Loyalsoldier/v2ray-rules-dat, аналогично.

Всё содержимое этой папки копируется в выходную папку сборки как есть (см. GodjiVpn.csproj,
<Content Include="Runtime\**\*">) — после сборки бинарники должны лежать в
bin\<Configuration>\net8.0-windows10.0.19041.0\win-x64\Runtime\ рядом с GodjiVpn.exe.
