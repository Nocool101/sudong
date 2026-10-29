# Quicker 调研分析报告

> ⚠️ 免责声明：本文仅为技术调研与学习之用，所涉 Quicker 商标归其所有者所有；本项目与 Quicker 官方无任何关联。
> 分析对象：`C:\Program Files\Quicker`（版本 1.45.5.0，.NET/WPF 应用）
> 分析方法：安装目录逆向 + 数据文件分析 + 运行时观察 + getquicker.net 官方文档调研（18 个页面）
> 配套文件：`Quicker调研报告.md`（官方文档详细调研）

---

## 一、产品定位

Quicker 是一个 **Windows 全局效率操作面板**：用户在任意软件中，通过鼠标中键/Ctrl 在原地弹出"面板窗口"或"轮盘菜单"，快速执行预先配置的"动作"（打开文件、发送文本、模拟按键、运行脚本、控制浏览器、截图 OCR 等）。

核心价值：**把高频重复操作抽象为可复用、可参数化、可共享的"动作"，并通过一个始终跟随鼠标的入口极速触达。**

---

## 二、核心交互模型（复刻重点）

### 2.1 双入口设计
Quicker 只有两种面板形态（官方确认，无方形/十字）：

| 形态 | 触发 | 操作方式 |
|---|---|---|
| **网格面板窗口** | 单击中键 / 快击 Ctrl（>200ms 视为普通键）/ 长按 / 画圈激活 | 弹出→点击动作→自动关闭；可开"按钮上松开即执行"实现按滑动松一键触发；滚轮悬停翻页 |
| **轮盘菜单** | 自设鼠标键按住（常用中键） | **按住立即移动**弹出，滑到扇形位松开触发；Esc 取消 |

### 2.2 面板结构
- 面板分上下两区：**上半 = 全局面板**（不随程序变），**下半 = 上下文面板**（按当前进程自动加载对应动作页，无则加载"通用"页）
- 流程：选中对象 → 弹出 → 点动作 → 自动关闭
- 腰栏：搜索框 / 锁定 / 钉住

### 2.3 轮盘结构（三圈）
- **内圈**（默认 8 位/圈，专业版 16）
- **外圈**（默认 8 位/圈）
- **扩展圈**（8 方向，不可见、覆盖全屏，滑出轮盘时激活，适合盲操作）
- 增强交互：位上连按 F1 重复触发；左 Shift = 动作右键菜单；右 Shift = 调试运行
- 滚轮上滚切全局轮盘、下滚切仅当前程序

### 2.4 场景系统（Context）
- **场景** = 全局 / 应用程序（按进程名匹配）/ 自定义
- 每场景独立配置：动作页、轮盘、手势、禁用项、黑名单、全屏禁用
- 程序场景有则用、否则回落全局
- 匹配规则支持通配、窗口类名、进程名

### 2.5 完整触发体系（本地逆向实证）
| 触发方式 | 说明 |
|---|---|
| 鼠标按钮触发 | 中键/X1/X2、Ctrl+中键/右键、长按、按住右键滑动、画圈激活、滚轮左击 |
| 键盘触发 | 单击 Ctrl、全局快捷键 |
| **扩展热键** | 引导键 + 动作键（可 3 键），类似 leader key |
| **文本指令** | 缩写词 + 触发字符（默认 Tab），支持正则传参 |
| **高级鼠标触发** | 鼠标动作 + 屏幕位置 + 修饰键 + 黑白名单的规则引擎，按序匹配 |
| 鼠标手势 | 轨迹识别 |
| 左键辅助 | 左键增强 |
| 事件触发 | 系统事件自动触发 |
| 触控笔按键 | 触控笔侧键 |

### 2.6 悬浮元素
- **悬浮按钮**：为常用动作创建桌面悬浮球 / 悬浮动作页
- **文本悬浮窗**：选中文本后出现悬浮标，点击弹出该文本可执行的动作
- **触发浮标**、动作提示窗、快速截图

---

## 三、数据模型（本地数据实证 + 官方文档确认）

### 3.1 存储布局（`%LOCALAPPDATA%\Quicker`）
```
Quicker/
├── data/
│   ├── quicker.db              # 全部定义数据（20MB，文件头非标准 SQLite 格式——已加密/混淆）
│   ├── app_search_cache.json   # 应用索引缓存（Name/Target/AppUserModelID/ScoreDelta...）
│   ├── local_search_files.txt  # 本地文件搜索索引
│   └── <uuid>_searchhistory.db # 搜索历史
├── states/
│   ├── _action_adorn.json      # 动作装饰（图标/角标/右键菜单），key = ActionId
│   └── state_<ActionId>.json   # 每个动作的持久化变量状态
├── ImageCache/<SHA1>.png       # 图标缓存（271 个）
├── bin/NativeMessageHost/      # 浏览器扩展通信宿主（ChromeAgent.exe, MV3）
├── Sounds/                     # info/snip/succeed/warning/wrong.mp3
└── logs/
```

### 3.2 数据库表（从 SQL 语句逆向）
```sql
LocalActionInfo(ActionId, ClickCount, LastClickTimeUtc, SyncState)   -- 本地动作使用统计
LocalSharedAction(SharedActionId, Revision, Data, InstallTimeUtc)    -- 从动作库安装的共享动作
ActionHistory(ActionId, BackupType, BackupTimeUtc)                   -- 动作历史备份
CommonData(SyncState, LocalOnly)                                     -- 通用数据/同步标记
```

### 3.3 状态文件格式（明文 JSON）
```json
{
  "ActionId": "00f9b5af-ccad-4464-9623-ab12fdbfff45",
  "IconUrl": null,
  "States": {
    "$var:单选还是多选": "True",
    "$var:l规则列表": "[]",
    "$var:c所有搜索引擎词典": "{...}",
    "sys:select_window_location": "..."
  }
}
```
**变量前缀约定：**
- `$var:` 动作级持久变量（值均为字符串序列化）
- `$var:l` 列表、`$var:c` 字典/复合、`$var:w` 窗口参数（命名约定）
- `sys:` 系统变量（如窗口位置）
- 每动作独立一个 state 文件 → **动作状态天然隔离**
- 官方：变量类型含文本/数字/布尔/词典/列表/表格/日期/动态对象，可标"状态变量"持久化

### 3.4 右键菜单 DSL（`_action_adorn.json` 实证 + 官方格式）
```
----                                    分隔线
[url:https://.../icon.png]菜单文本|点击后执行的动作
[fa:Light_CommentAltLines:#FC9724]关闭提示|关闭所有提示
[fa:Solid_UserCog:#008FD2]菜单文本|(tooltip)
```
- `[url:...]` 网络图标、`[fa:...]` FontAwesome 矢量图标
- `|` 后是点击后发送的动作命令；`(tooltip)` 提示；缩进 = 子菜单
- 点击即以该参数运行动作，动作内判断 `quicker_in_param` 分支

### 3.5 动作数据结构
一个**动作** = 图标 + 名称 + 触发方式 + **步骤列表** + 变量定义 + 右键菜单
- **简单动作 6 种**（官方）：模拟按键、运行程序/打开文件（含命令）、打开网址、发送文本、切换动作页、执行脚本
- **快速操作 8 种**（轮盘/文本指令/扩展热键/左键辅助/高级鼠标触发共用）：无操作、发送快捷键、键入内容（按键语法）、键入纯文本、粘贴纯文本、粘贴 HTML、多步骤输入、常用功能、运行 Quicker 动作、打开或运行、键鼠控制
- **组合动作** = 有序步骤 + 变量 + 子程序；官方模块约 130 个、11 大类（完整清单见调研报告附录 A）
- 步骤 = 模块 + 输入参数 + 输出参数；参数支持静态值、变量、文本插值、表达式
- 文本参数支持 `{cliptext}`/`{selection}`/`{0:日期格式}`/`FILE:路径` 插值
- 动作属性：图标（图片/SVG/字符）、**可见性表达式**、快捷键、滚轮连续触发

---

## 四、技术架构

```
QuickerStarter.exe（开机启动引导）
    └─ Quicker.exe（主进程，常驻托盘，PID 单实例）
         ├─ 全局低层键鼠钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）
         ├─ 轮盘/面板窗口（WPF，置顶、无焦点、透明穿透）
         ├─ 动作执行引擎（步骤解释器）
         │    ├─ 内置步骤类型（约 130 个模块）
         │    ├─ C#/JS/Python 脚本沙箱
         │    └─ 子程序调用（动作内/公共/网络共享）
         ├─ 加密 SQLite 存储
         ├─ 场景匹配引擎（前台窗口 → 面板切换）
         ├─ ChromeAgent.exe（浏览器扩展 Native Messaging 桥，MV3）
         ├─ LPAgent.exe（低权限模式脚本代理进程）
         └─ 云同步/动作库客户端（客户端单向触发同步）
```

**运行时能力（DLL 依赖实证）：**
| 能力 | 依赖库 |
|---|---|
| C# 脚本 | Microsoft.CodeAnalysis.CSharp + CSScriptLibrary（CS-Script） |
| JavaScript 脚本 | Jint |
| Python 脚本 | Python.Runtime（pythonnet，3.7–3.12） |
| 表达式求值 | Z.Expressions.Eval / DynamicExpresso |
| 全局键鼠钩子/模拟 | WindowsInput + PInvoke.User32 |
| UI 自动化 | FlaUI.UIA3 |
| AutoIt 兼容 | AutoItX3 |
| 本地文件秒搜 | Everything64.dll |
| 内嵌浏览器 | WebView2 |
| Office 文档 | Microsoft.Office.Interop.* / NPOI |
| 二维码 | QRCoder / zxing |
| 音频 | NAudio |
| 浏览器控制 | ChromeAgent.exe + Native Messaging (MV3 扩展，CDP) |
| OCR | 腾讯云 API 等 |
| 通知 | ToastNotifications |
| UI 框架 | HandyControl + WPF |

**关键设计决策：**
1. **加密 SQLite**：保护用户数据与配置
2. **每动作独立 state 文件**：状态隔离 + 增量同步友好
3. **纯文本 DSL**（右键菜单）：低门槛扩展
4. **多脚本引擎并存**：C#（.NET 原生）、JS（跨平台习惯）、Python（生态）
5. **变量全字符串化**：JSON 序列化简单，脚本边界清晰
6. **Native Messaging 桥**：浏览器控制不走 COM，扩展 + 独立 exe 通信
7. **钩子重载机制**：CPU 繁忙超时钩子被系统回收，需自动检测重载

---

## 五、复刻建议

### 5.1 MVP 范围（第一阶段）
1. 全局钩子：单击中键 / 快击 Ctrl 弹出网格面板
2. 轮盘：按住触发键滑动选择（内圈 8 格即可）
3. 动作模型：动作 = 步骤列表，首批步骤类型：
   - 打开文件/文件夹/网址
   - 发送文本 / 模拟按键
   - 运行程序 / CMD
   - 等待 / 如果-否则 / 重复
   - 组合动作 / 子程序调用
4. 变量系统（`$var:` 持久化，动作级隔离，JSON state 文件）
5. 场景系统（全局 + 按进程匹配的上下文面板）
6. 动作编辑器（步骤列表 UI + 参数编辑）
7. 托盘常驻 + 开机自启

### 5.2 第二阶段
- C#/JS 脚本步骤、表达式求值
- 文本指令（缩写词 + Tab 触发）
- 扩展热键（引导键体系）
- 悬浮按钮 / 文本悬浮窗
- Everything 搜索集成
- 动作右键菜单 DSL
- 高级鼠标触发规则引擎

### 5.3 技术选型建议
| 层 | 推荐 | 理由 |
|---|---|---|
| 语言/UI | C# + WPF（或 Avalonia） | 与原版同构，Win API 调用最顺；WPF 生态（HandyControl）可参考 |
| 存储 | SQLite + 每动作 state JSON | 与实证结构一致 |
| 脚本 | 先 C#（Roslyn）+ JS（Jint） | 覆盖 90% 场景 |
| 钩子 | 自封装 WH_LL + 重载检测 | 核心 |
| 搜索 | Everything SDK 可选 | 后期 |

---

## 六、已知待对齐项
- 轮盘精确外观参数（圈半径/间距/动画曲线）
- 云同步协议细节（单向触发式的冲突处理）
- 动作库分享协议（如需兼容生态）
