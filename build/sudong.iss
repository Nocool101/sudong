; 速动 (QuickLauncher) 安装脚本 — Inno Setup 6
; 安装后落地《第2步-UIAccess改造计划》全部要点：
;   1) 安装到 Program Files（UIAccess 硬前提之一：安全位置）
;   2) exe 已在发布时经 signtool 签名（硬前提之二：数字签名）
;   3) 安装时把自签名证书导入“受信任的根 / 受信任的发布者”（硬前提之三：证书链受信任）
;      三项同时满足 ⇒ asInvoker + uiAccess=true 生效：不弹 UAC、子进程普通权限、可操作 yuzu/任务管理器等管理员窗口 UI
;   4) 计划任务·登录时·/RL LIMITED 开机自启（普通权限，配合 UIAccess）

#define MyAppName "速动"
#define MyAppNameEn "QuickLauncher"
#define MyAppVersion "1.0.1"
#define MyAppExe "QuickLauncher.App.exe"

[Setup]
AppId={{8F3A9C2E-1B7D-4E6A-9A5C-2D4F6B8E0A1C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion=1.0.1.0
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
; 相对路径基于【本脚本所在目录】(build\) 解析，与 scripts\sudong.iss 保持一致：
;   ..\build\publish\*  → 仓库根的 build\publish（由 pack-selfsigned.ps1 产出）
;   ..\src\...          → 仓库根的 src
;   ..\发布版\v1.0.0    → 仓库根的 发布版\v1.0.0
; 【为什么不用绝对路径】绝对路径会把维护者的本机磁盘布局写进公开仓库
; （审计报告 P-4「源码与文档残留作者本机磁盘布局」），且对其他贡献者不可用。
OutputDir=..\发布版\v1.0.0
; 产物名用纯 ASCII：中文文件名在 GitHub Release 上传环节会被编码降级成 "-"（实测 v1.0.0
; 变成 "-Setup.exe" / "-v1.0.0-win-x64.zip"），用户下载到的文件名一眼像恶意文件。
; 版本号取自 MyAppVersion，避免文件名与安装包内版本信息不一致。
OutputBaseFilename=Sudong-Setup-{#MyAppVersion}
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
; 【关于证书导入为什么不是可选项】
; 本安装包内置的是「UIAccess 版」主程序（manifest: asInvoker + uiAccess="true"）。
; Windows 对 UIAccess 程序有硬性检查：签名必须被系统信任、且装在 Program Files 等安全位置，
; 否则【进程根本无法创建】（CreateProcess 返回 740 ERROR_ELEVATION_REQUIRED）。
; 也就是说：证书不导入 → 程序装得上但起不来 → 那不是真正的「选项」。
; 因此这里采用「必须导入 + 安装前明确告知 + 卸载可撤销」，
; 并在告知框里给出真正的替代选择：GitHub Release 上的开源版安装包（不导入任何证书）。
;
; 另注：Inno Setup 6 的 [Tasks] 不支持 checked 标志（只有 unchecked/exclusive 等），
; 所以「默认勾选」也无法用任务标志实现。

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; \
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
;    这是必需步骤，不是可选项——证书不受信时 UIAccess 程序根本无法启动（740）。
;    用户知情由 [Code] 的 CurStepChanged 在安装开始前明确告知。
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
; 撤销证书信任：把安装时导入的自签名证书从受信任存储移除（按【主题名】删除）。
; 卸载程序以管理员身份运行，删除受信任根证书是允许的。
; 若用户已手动删过，-delstore 找不到目标会返回非零码，无害（RunOnceId 保证只跑一次）。
Filename: "certutil.exe"; Parameters: "-delstore Root ""CN=QuickLauncher Dev"""; Flags: runhidden; RunOnceId: "DelRootCert"
Filename: "certutil.exe"; Parameters: "-delstore TrustedPublisher ""CN=QuickLauncher Dev"""; Flags: runhidden; RunOnceId: "DelPublisherCert"

[Code]
// 安装文件前结束正在运行的旧实例（安装程序已是管理员，可结束管理员旧实例，避免占用导致复制失败）
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM QuickLauncher.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// 安装【开始前】明确告知「证书信任」这件事：做了什么、为什么、意味着什么、如何撤销。
// 导入受信任根证书是一次性的信任授予，用户必须知情。
// 这里不给「跳过」选项——证书不受信时 UIAccess 程序根本无法启动（740），
// 那不是真正的选择；真正的替代路径是使用 GitHub Release 上的开源版安装包（不导入任何证书）。
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // 静默安装（/SILENT、/VERYSILENT）时跳过告知框——否则自动化安装会卡在这个对话框上。
  // 静默模式的使用者一般是脚本/无人值守环境，告知改为写在 README 与发布说明里。
  if (CurStep = ssInstall) and (not WizardSilent) then
  begin
    MsgBox('安装前请注意 —— 本安装包会向系统添加一张证书：' + #13#10 + #13#10 +
           '  证书：CN=QuickLauncher Dev（速动自签名代码签名证书）' + #13#10 +
           '  导入到：受信任的根证书颁发机构' + #13#10 +
           '          受信任的发布者' + #13#10 + #13#10 +
           '【为什么】速动使用 Windows 的 UIAccess 机制，从而能在' + #13#10 +
           '任务管理器、yuzu 等「以管理员身份运行的窗口」内唤出面板。' + #13#10 +
           'Windows 要求 UIAccess 程序必须「已签名 + 证书被系统信任 +' + #13#10 +
           '安装在 Program Files」，三者缺一程序就无法启动（错误 740）。' + #13#10 + #13#10 +
           '【意味着什么】导入后，本机上由这张证书签名的程序都会被系统信任。' + #13#10 +
           '【如何撤销】卸载本程序会自动删除它；也可在「管理证书」中手动删除。' + #13#10 + #13#10 +
           '【不想加这张证书？】请改用 GitHub Release 上的开源版安装包' + #13#10 +
           '（不导入任何证书，但也不支持在管理员窗口内唤出面板）。',
           mbInformation, MB_OK);
  end;
end;
