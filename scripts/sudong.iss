; 速动 (QuickLauncher) 安装脚本 — 开源/CI 版（相对路径，无代码签名）
; 供 .github/workflows/release.yml 的 Inno 步骤使用（iscc 需在仓库根目录运行）。
; 与 build/sudong.iss 的区别：使用相对路径、去掉自签名证书导入（开源无证书、uiAccess=false 无需签名）。

#define MyAppName "速动"
#define MyAppNameEn "QuickLauncher"
#define MyAppVersion "1.0.0"
#define MyAppExe "QuickLauncher.App.exe"

[Setup]
AppId={{8F3A9C2E-1B7D-4E6A-9A5C-2D4F6B8E0A1C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion=1.0.0.0
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=速动-Setup
SetupIconFile=..\src\QuickLauncher.App\assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; \
    Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 开机自启：计划任务·登录时·普通权限(LIMITED)
Filename: "schtasks.exe"; Parameters: "/Create /TN ""QuickLauncher"" /TR ""\""{app}\{#MyAppExe}\"""" /SC ONLOGON /RL LIMITED /F"; Flags: runhidden; StatusMsg: "正在配置开机自启（计划任务）..."

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""QuickLauncher"" /F"; Flags: runhidden; RunOnceId: "DelStartupTask"

[Code]
// 安装文件前结束正在运行的旧实例（安装程序已是管理员，避免占用导致复制失败）
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM QuickLauncher.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
