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

[Registry]
; 开机自启：HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run（由 explorer.exe 中完整性启动）。
; 本版 manifest 为 uiAccess="false"，用计划任务也能启动；但统一走 Run 项可与 UIAccess 版
; （build\sudong.iss）共用同一套自启逻辑，减少分支差异。卸载时由 uninsdeletevalue 自动清理。
; ValueData 三层引号 = 一个字面 "，故写入值为 "{app}\{#MyAppExe}"（路径含空格必须加引号）。
; 注意：PrivilegesRequired=admin 下写 HKCU 会触发 Inno 的 UsedUserAreasWarning；
; 程序自身 App.OnStartup 中的 StartupService.Apply(true) 会用当前用户 HKCU 幂等重写，作为主保障。
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#MyAppNameEn}"; ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue

[Run]
; 安装时清理旧版 schtasks 计划任务残留（该任务由安装程序创建，普通令牌进程删除会
; “拒绝访问”，必须在提权的安装程序上下文中执行；见 build\sudong.iss 同款注释）。
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""QuickLauncher"" /F"; Flags: runhidden

[UninstallRun]
; 清理旧版可能残留的 schtasks 计划任务（一次性迁移，防止两处并存）
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""QuickLauncher"" /F"; Flags: runhidden; RunOnceId: "DelLegacyStartupTask"

[Code]
// 安装文件前结束正在运行的旧实例（安装程序已是管理员，避免占用导致复制失败）
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM QuickLauncher.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
