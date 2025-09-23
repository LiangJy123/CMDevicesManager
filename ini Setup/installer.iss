#define MyAppName        "LCD Manager"
#define MyAppExeName     "LCDManager.exe"
#define MyCompany        "ELINK"
#define MyAppVersion     "0.0.2"
#define MyPublishDir     "..\\bin\\release\\net8.0-windows10.0.19041.0\\publish"

[Setup]
AppId={{6F8C7F7F-5E1E-4BC4-9D7E-4F0F9D012345}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyCompany}
DefaultDirName={autopf}\{#MyCompany}\{#MyAppName}
DisableDirPage=no
DefaultGroupName={#MyAppName}
OutputBaseFilename=LCDManagerSetup-{#MyAppVersion}
OutputDir=.\Output
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=no
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\AppIcon.ico

; 管理员权限
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "en";    MessagesFile: "compiler:Default.isl"
Name: "ChineseSimplified"; MessagesFile: "compiler:Languages\\ChineseSimplified.isl"
Name: "ChineseTraditional"; MessagesFile: "compiler:Languages\\ChineseTraditional.isl"

[CustomMessages]
; --- Desktop Icon ---
en.CreateDesktopIcon=Create a &desktop icon
ChineseSimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
ChineseTraditional.CreateDesktopIcon=建立桌面捷徑(&D)

; Group: Additional Icons
en.AdditionalIcons=Additional icons:
ChineseSimplified.AdditionalIcons=附加图标:
ChineseTraditional.AdditionalIcons=附加圖示:

; Autostart
en.Autostart=Run at Windows &startup
ChineseSimplified.Autostart=开机自动运行(&S)
ChineseTraditional.Autostart=開機自動執行(&S)

; Group: Startup Options
en.StartupOptions=Startup options:
ChineseSimplified.StartupOptions=启动选项:
ChineseTraditional.StartupOptions=啟動選項:

; Launch after install (Finish page checkbox)
en.LaunchApp=Launch {#MyAppName}
ChineseSimplified.LaunchApp=运行 {#MyAppName}
ChineseTraditional.LaunchApp=執行 {#MyAppName}

; (可再补充更多自定义本地化条目)

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: autostart;  Description: "{cm:Autostart}"; GroupDescription: "{cm:StartupOptions}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"; \
    ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\\{#MyAppExeName}"""; Tasks: autostart

[Run]
; 使用多语言描述
Filename: "{app}\\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\\{#MyCompany}\\{#MyAppName}"