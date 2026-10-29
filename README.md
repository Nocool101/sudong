# 速动 (QuickLauncher)

受 [Quicker](https://getquicker.net) 启发的 Windows 全局效率操作面板（C# + WPF / .NET 7）：
在任意软件中通过鼠标中键/Ctrl 唤出面板，快速执行预先配置的"动作"。

> ⚠️ **免责声明**：本项目为独立的开源实现，与 Quicker 官方**无任何关联**；Quicker 是其各自所有者的商标。
> 本仓库中的 Quicker 相关内容（含 `docs/`）仅作技术调研与学习之用。

> 设计与调研文档见 [`docs/Quicker调研分析报告.md`](docs/Quicker调研分析报告.md) 与
> [`docs/Quicker调研报告.md`](docs/Quicker调研报告.md)。

## 功能特性

### 双入口交互
- **网格面板**：单击鼠标中键 / 快击 Ctrl（<200ms）弹出；点击动作后自动关闭；滚轮翻页；失活关闭
- **桌面悬浮按钮**：可拖动的悬浮球，点击弹出面板（设置中开启）

### 面板双区结构（参考原版）
- **上区 = 全局动作页**：无论在哪个程序打开都显示
- **下区 = 上下文动作页**：仅当当前前台程序匹配到对应场景（应用程序/自定义）时显示；未匹配则显示创建引导
- **弹窗内自管理**（无需打开 App）：下区与上区完全同构——没有专用页时同样显示「＋」网格，点「＋」即**静默创建该程序的专用动作页**并把动作放进去；⚙ 菜单编辑全局/专用动作页；磁贴支持**跨区拖放**（拖到空格=移动、拖到磁贴=互换区）
- **隔离性**：专用磁贴只在对应程序打开面板时出现；其他程序只有全局区；场景保存后匹配快照热更新，立即可见

### 场景系统
- 全局场景 + 应用程序场景（按进程名/窗口类名/标题匹配，支持 `*` 通配）
- 场景优先级：自定义 > 应用程序；全局场景固定为上区内容
- 每个场景独立配置面板页（场景编辑器内可新建专用页并直接编排磁贴）

### 动作体系
- **简单动作**（9 种）：打开网址/文件/文件夹、运行程序、运行命令（RunCmd）、发送文本、模拟按键、加载动作页（SwitchPage）、执行脚本（RunScript：C#/JS/Python/脚本文件，语言下拉切换）
- 每种简单动作在编辑器中渲染**专属参数表单**（文件浏览、按键捕获、页面下拉、多行代码、变量插入），与组合动作步骤共用参数编辑器
- **应用图标提取**：从 exe/dll/icl（含开始菜单 .lnk 快捷方式）提取内嵌图标缓存为 PNG（对应原版 ImageCache 机制），输入程序路径后自动提取；面板/编辑器预览均显示真实图标
- **组合动作**：有序步骤编排，30+ 内置步骤：
  - 常用：打开/运行/发送、等待、注释
  - 程序流：如果/否则、每个（遍历）、重复、跳出/继续、停止、步骤组
  - 变量：赋值（支持表达式）、列表操作、用户输入、用户选择、提示消息
  - 剪贴板：读写文本、获取选中文本（Ctrl+C 语义）、文件列表
  - 界面交互：激活窗口、等待窗口
  - 系统：日期时间、播放声音、运行 CMD/PowerShell
  - 文件：读写文本文件
  - 网络：联网搜索（百度/谷歌/必应）
  - **脚本**：运行 C# 代码（Roslyn）、运行 JS 代码（Jint）、运行 Python（外部解释器）、运行脚本文件（.csx/.js/.py/.ps1/.bat/.ahk）
- **变量系统**：文本/数字/布尔/列表等类型，`$var:` 前缀持久化到每动作独立 state 文件
- **表达式**：轻量递归下降求值器（and/or/not、比较、算术、括号、变量）
- **文本插值**：`{变量名}`、`{日期:yyyy-MM-dd}`

### 触发体系
- 鼠标按钮（中键/X1/X2、长按、画圈激活配置项）
- 键盘（快击 Ctrl、全局快捷键）
- **文本指令**：缩写词 + 触发字符（默认 Tab），支持正则传参、退格修正、进程绑定
- **扩展热键**：引导键 + 动作键（如 CapsLock → P），支持修饰键三键组合（如 Ctrl+Shift+CapsLock → P）与引导键单独触发

### 数据与管理界面
- SQLite 存储（动作/场景/页面/指令/热键规则/使用统计）
- 每动作 state JSON（`%LOCALAPPDATA%\QuickLauncher\states\state_<ActionId>.json`）
- 管理窗口：动作管理（列表/编辑器/步骤编排、导入/导出 JSON）、场景管理、指令管理、扩展热键、设置
- 托盘常驻、开机自启（计划任务·登录时·普通权限）、挂起/恢复
- 命令行：`QuickLauncher.App.exe --run <动作Id>` 直接执行动作

## 快速开始

```bash
# 构建
dotnet build

# 运行测试（14 个单元测试）
dotnet test

# 启动应用
dotnet run --project src/QuickLauncher.App
```

启动后：
1. **单击鼠标中键** 弹出面板（内置 5 个示例动作）
3. 在"动作管理"中新建/编辑动作；组合动作可添加脚本步骤
4. 在"设置"中开启桌面悬浮按钮

## 权限模型

**开源默认：`asInvoker`（普通权限）** —— 直接 `dotnet run --project src/QuickLauncher.App` 即可运行，启动不弹 UAC。（见 `src/QuickLauncher.App/app.manifest`）

**可选增强：UIAccess** —— 若你想在 **yuzu / 任务管理器等“以管理员运行”的窗口**内也能唤出面板并发送键鼠，可启用 `uiAccess="true"`。但这有硬前提，缺一即被系统忽略，且**未签名时会被 Windows 拒绝启动（错误 740/577）**：
1. 可执行文件**已代码签名**（本机自签名并导入“受信任的根”，或公共信任的 OV/EV 证书）；
2. **安装在 `C:\Program Files\`** 等安全位置；
3. 证书链连到本机“受信任的根证书颁发机构”。

因此开源版默认关闭 UIAccess。需要时：用正式证书签名跑 `scripts\sign-and-pack.ps1`；本机自签验证跑 `scripts\setup-signing.ps1` + `scripts\deploy.ps1`（均需管理员 PowerShell），再把 `app.manifest` 的 `uiAccess` 改为 `true`。

> 开机自启由 `Services/StartupService.cs` 以「计划任务·登录时·普通权限(LIMITED)」管理；运行期数据写在 `%LOCALAPPDATA%\QuickLauncher`（普通权限可写，与安装位置解耦）。

## 安全与隐私
- **完全本地运行**：无遥测、无统计上报、无任何网络请求（唯一联网行为是「联网搜索」动作交给本地默认浏览器打开）。所有数据仅存本机 `%LOCALAPPDATA%\QuickLauncher`，**不上传任何服务器**。
- 详见 [PRIVACY.md](PRIVACY.md) 与 [SECURITY.md](SECURITY.md)。
- **权限**：以普通权限（`asInvoker`）运行、启动不弹 UAC；脚本动作（C#/JS/Python）在你本机以你的用户权限执行，等同于手动运行，请仅运行你信任的脚本。
- **调试日志默认关闭**，且不记录窗口标题，可在设置中按需开启。

## 下载校验（v1.0.0）
发布产物未做 Authenticode 数字签名，请用 SHA256 自行校验完整性：

| 文件 | SHA256 |
|---|---|
| `速动-v1.0.0-win-x64.zip` | `be42702f2e2207051e7807b706b48fb57443691a5645c9c1e751ab4ad48c1899` |
| `速动-Setup.exe` | `d735def17918a35b3fdfae87899bf1868bf4546e9fdceb258d887101ae41dd2c` |

```powershell
Get-FileHash -Algorithm SHA256 .\速动-v1.0.0-win-x64.zip
Get-FileHash -Algorithm SHA256 .\速动-Setup.exe
```

## 项目结构

```
QuickLauncher/
├── QuickLauncher.sln
├── src/
│   ├── QuickLauncher.Core/          # 核心库（无 UI 依赖）
│   │   ├── Models/                  # 数据模型（Action/Scene/Page/TextCommand...）
│   │   ├── Storage/                 # SQLite + state JSON 仓库
│   │   ├── Engine/                  # 步骤解释执行器 + 表达式求值
│   │   ├── Steps/                   # 30+ 内置步骤类型
│   │   ├── Scripting/               # C#(Roslyn) / JS(Jint) / Python 脚本引擎
│   │   └── Services/                # 服务接口（通知/对话框/面板）
│   └── QuickLauncher.App/           # WPF 应用层
│       ├── Hooks/GlobalHooks.cs     # 全局键鼠钩子（含看门狗重载）
│       ├── Interaction/             # 交互状态机、文本指令、扩展热键监听
│       ├── Scenes/SceneMatcher.cs   # 场景匹配
│       ├── Services/PanelService.cs # 面板窗口管理
│       └── Views/                   # 面板、编辑器、设置等窗口
└── tests/QuickLauncher.Tests/       # xunit 测试（14 个）
```

## 架构要点

1. **钩子层**：`WH_KEYBOARD_LL` + `WH_MOUSE_LL` 低层钩子，看门狗定时检测句柄失效自动重装
2. **交互状态机**：`Idle → MiddlePressed`，按下计时、抬起 <350ms 判定为单击切换面板
3. **无焦点窗口**：面板使用 `WS_EX_NOACTIVATE`，不抢焦点；面板外点击通过钩子检测关闭
4. **执行引擎**：步骤类型注册表 + 解释执行，容器步骤通过 `ChildrenRunner` 回调递归
5. **自触发防护**：动作执行期间 `Suppress()` 抑制钩子事件，避免 SendInput 死循环
6. **存储**：SQLite 存定义数据，每动作独立 state JSON 存持久变量（状态隔离 + 同步友好）

## 已知限制 / 后续方向

- 高级鼠标触发规则（规则引擎）已有数据模型，执行层未接入
- 云同步、动作库分享未实现
- 与第三方手势软件（Listary 等）存在钩子竞争（原版 Quicker 亦然）
- Python 脚本通过外部解释器执行（未内嵌 pythonnet）

## 技术栈

- .NET 7 / C# / WPF
- Microsoft.Data.Sqlite + Newtonsoft.Json
- Microsoft.CodeAnalysis.CSharp.Scripting（Roslyn）
- Jint（JavaScript）
- xunit（测试）
