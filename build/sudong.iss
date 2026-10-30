; 速动 (QuickLauncher) 安装脚本 — Inno Setup 6
; 安装后落地《第2步-UIAccess改造计划》全部要点：
;   1) 安装到 Program Files（UIAccess 硬前提之一：安全位置）
;   2) exe 已在发布时经 signtool 签名（硬前提之二：数字签名）
;   3) 安装时把自签名证书导入“受信任的根 / 受信任的发布者”（硬前提之三：证书链受信任）
;      三项同时满足 ⇒ asInvoker + uiAccess=true 生效：不弹 UAC、子进程普通权限、可操作 yuzu/任务管理器等管理员窗口 UI
;   4) 计划任务·登录时·/RL LIMITED 开机自启（普通权限，配合 UIAccess）

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
OutputDir=E:\AI 工作目录\应用启动器\发布版\v1.0.0
OutputBaseFilename=速动-Setup-1.0.0
SetupIconFile=E:\AI 工作目录\应用启动器\src\QuickLauncher.App\assets\app.ico
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
Source: "E:\AI 工作目录\应用启动器\build\publish\*"; DestDir: "{app}"; \
    Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 0) 清理旧版（schtasks 计划任务方案）残留的开机自启任务。
;    必须以【管理员身份】删除：该任务由安装程序创建，普通令牌进程（含本程序自身的
;    RemoveLegacyTask）删除会「拒绝访问」，故放在安装时执行（本段所有条目均在
;    提权的安装程序上下文中运行）。RunOnceId 保证每台机器只尝试一次。
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""QuickLauncher"" /F"; Flags: runhidden; StatusMsg: "正在清理旧版开机自启（计划任务）..."
; 1) 导入自签名证书到“受信任的根证书颁发机构”（UIAccess 硬前提）
Filename: "certutil.exe"; Parameters: "-addstore Root ""{app}\ql-dev.cer"""; Flags: runhidden; StatusMsg: "正在配置代码签名信任（根证书）..."
; 2) 导入到“受信任的发布者”
Filename: "certutil.exe"; Parameters: "-addstore TrustedPublisher ""{app}\ql-dev.cer"""; Flags: runhidden; StatusMsg: "正在配置代码签名信任（发布者）..."
; 3) 开机自启：注册表 HKCU Run 项（见下方 [Registry] 段，Flags: uninsdeletevalue 随卸载自动清理）。
;    为什么【不能】用计划任务：本程序为 asInvoker + uiAccess="true"，而 Windows 只允许
;    「中完整性」进程启动 UIAccess 程序。Task Scheduler 服务自身以 LocalSystem（高完整性）运行，
;    schtasks 的 /RL LIMITED 仅降权【目标进程】、并不改变 Task Scheduler 作为父进程的身份，
;    因此由它启动本程序必返回 740(ERROR_ELEVATION_REQUIRED)，表现为"计划任务就绪但登录后静默不启动"
;    （实测：Start-ScheduledTask 触发 → LastTaskResult 0x800702E4，进程未创建）。
;    HKCU Run 项由 explorer.exe（中完整性桌面外壳）在用户登录后启动，符合 UIAccess 启动模型。
; 4) 安装后不自动启动：uiAccess="true" 的程序不应由「高完整性」的安装程序父进程以 CreateProcess 启动，
;    否则报 CreateProcess failed; code 740。这正是原版 Quicker"不能被管理员启动"的体现。
;    装完后请从「开始菜单 / 桌面」的「速动」快捷方式启动（资源管理器为中完整性，符合 UIAccess 启动模型）；
;    下次开机则由 [Registry] 的 Run 项自动以普通+UIAccess 身份启动，无需用户任何操作。

[Registry]
; 开机自启：HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
; ValueData 三层引号 = 一个字面 "，故写入值为 "{app}\{#MyAppExe}"（路径含空格，必须整体加引号，
; 否则 explorer 启动时会在第一个空格处截断）。uninsdeletevalue 使卸载时自动删除该值。
;
; 【关于 Inno 的 UsedUserAreasWarning】PrivilegesRequired=admin（写 Program Files + 导入证书必须提权）
; 与 HKCU 组合会触发该警告：管理安装模式下此处的 HKCU 写入指向【运行安装程序的那个用户】。
; 个人电脑「自己安装自己用」完全正确；但若管理员安装、他人使用，这里写的 Run 项会落错地方。
; 因此它只是「安装后立即生效」的便利项，真正的保障是程序自身：
; App.OnStartup 中 StartupService.Apply(true)（RunAtStartup 默认 true）会用【当前运行用户】的
; HKCU 幂等重写 Run 项，用户首次从开始菜单/桌面启动一次后即被纠正，后续开机自启稳定。
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#MyAppNameEn}"; ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue

[UninstallRun]
; 清理旧版可能残留的 schtasks 计划任务（UIAccess 下该任务必然 740 失败，留着只是噪音）
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""QuickLauncher"" /F"; Flags: runhidden; RunOnceId: "DelLegacyStartupTask"

[Code]
// 安装文件前结束正在运行的旧实例（安装程序已是管理员，可结束管理员旧实例，避免占用导致复制失败）
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM QuickLauncher.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
