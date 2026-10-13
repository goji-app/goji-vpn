; Установщик Godji VPN (Inno Setup 6). Собирается из готового self-contained
; publish (windows\publish_stage) — см. windows\build-installer.ps1, который
; сначала делает dotnet publish, потом компилирует этот скрипт через ISCC.exe.

#define MyAppName "Godji VPN"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.74"
#endif
#define MyAppPublisher "Godji"
#define MyAppExeName "GodjiVpn.exe"
#define MyAppMutex "GodjiVpn.SingleInstance.Mutex"
#define SourceDir "..\publish_stage"
#define IconFile "..\GodjiVpn\Assets\Images\app.ico"

[Setup]
AppId={{2E6F1B7A-6C2B-4B7B-9C0A-5D6E0E5A9B41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Приложение всегда запускается с requireAdministrator (TUN меняет системную
; маршрутизацию) — ставим установщик тоже от администратора, чтобы не было
; двойного UAC-запроса и чтобы Program Files был доступен на запись.
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=GodjiVpn-Setup-{#MyAppVersion}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex={#MyAppMutex}
CloseApplications=yes
RestartApplications=no
; Игнорируем битые пары суррогатов юникода (фоновая совместимость) — не нужно.

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Флаг "postinstall" сам по себе по умолчанию запускает программу с правами
; ИСХОДНОГО (неповышенного) пользователя, а не установщика — это защита от
; случайно оставшегося висеть elevated-процесса. Плюс сам вызов идёт через
; CreateProcess, который в принципе игнорирует манифест requireAdministrator
; и просто отказывает кодом 740 (ERROR_ELEVATION_REQUIRED), кто бы его ни звал.
; "shellexec" переключает вызов на ShellExecute — тот умеет читать манифест
; и корректно обрабатывает повышение; "runascurrentuser" говорит взять уже
; повышенный токен самого установщика, чтобы не спрашивать UAC повторно.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: shellexec runascurrentuser postinstall skipifsilent

[UninstallRun]
; На всякий случай гасим ядро (sing-box/xray), если установщик закрытия
; главного процесса (AppMutex/CloseApplications) не успело их убить —
; они самостоятельные процессы, не дочерние в понимании Job-объекта.
Filename: "{cmd}"; Parameters: "/C taskkill /IM sing-box.exe /F & taskkill /IM xray.exe /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillCoreProcesses"
