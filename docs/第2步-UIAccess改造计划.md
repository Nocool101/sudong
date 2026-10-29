# 第 2 步计划：改用 UIAccess（照抄 Quicker 原版权限模型）

> 状态：**代码与脚本已实施并加固**。已核实 `app.manifest`=asInvoker+uiAccess、`StartupService`=/RL LIMITED、csproj 引用 manifest；Release 构建通过（0 错误）。本次执行定位并修复了阻碍落地的问题：① 两个 `.ps1` 缺 UTF-8 BOM → PowerShell 5.1 按 GBK 误读中文、语法解析失败（已补 BOM，这很可能是此前部署/签名始终没跑成的真正原因）；② `deploy.ps1` 依赖 PATH 中的 signtool（本机装在 Windows Kits，已改为自动定位）；③ `setup-signing.ps1` 使用本环境不支持的 `-CodeSigningCert` 参数（已改为按 EKU 1.3.6.1.5.5.7.3.3 手动过滤）；④ 新增 `scripts/bootstrap.ps1` 一键串联「证书→部署+签名」。
> 状态（开源路线）：**默认关闭 UIAccess**。开源版以 `asInvoker` 直接可运行、不依赖代码签名；本步（UIAccess + 签名 + 计划任务）作为**可选增强**保留在 `scripts/` 与本文档中，需要者自行签名 + 装到 Program Files 后把 `app.manifest` 的 `uiAccess` 改为 `true` 即可启用。
> 目标：在**保留"开机自启 + 在 yuzu/任务管理器等管理员应用内可用"**的前提下，去掉 `requireAdministrator` 的代价（每次启动弹 UAC、子进程全继承管理员、外部启动动作/网页预览失效等）。
> 方法：照抄原版 Quicker 的权限模型 —— `asInvoker + UIAccess(uiAccess=true)`，配合**代码签名 + 安装到 Program Files**。
> 关联文档：`Quicker复刻分析报告.md`、`Quicker调研报告.md`

---

## 一、背景与现状

### 1.1 已完成（第 1 步）
- 开机自启由"注册表 Run 项"迁移为 **Windows 计划任务（登录时·最高权限）**，`StartupService` 负责创建/删除；`App.OnStartup` 做一次性迁移。
- 程序 manifest 设为 `requireAdministrator`，使普通权限下收不到的管理员窗口输入（yuzu）得以捕获、`SendInput` 得以送达。

### 1.2 `requireAdministrator` 的代价（要消除的问题）
1. **每次手动启动弹 UAC**（体验差；且 `dotnet run` 直接报 740 无法启动）。
2. **子进程全部继承管理员权限**：通过 QuickLauncher 启动的程序都变成管理员运行。
3. Quicker 官方（作者 CL）明确列出的管理员运行副作用：
   - 不能从外部启动动作（`--run` / 网站/其它程序调用）；
   - 不能在 Quicker 网页端点击按钮预览动作、安装外观；
   - 通过其运行的程序无法以普通权限运行。

### 1.3 根因回顾
- yuzu 等以**管理员（高完整性）**运行；普通（中完整性）进程的全局低层钩子与 `SendInput` 会被 **UIPI** 特权隔离挡掉 → 表现为"无法唤起、动作无反应"。
- 实测：yuzu = `S-1-16-12288`（高），QuickLauncher = `S-1-16-8192`（中）。

---

## 二、原版 Quicker 的机制（调研结论）

> **作者 CL 官方原话**（getquicker.net/QA/2517）：
> "Quicker 是使用微软官方的提权机制，**比一般的权限高一些，又比管理员权限低一些**，因此它可以在**任务管理器**这类窗口中工作，但又比较安全。**Quicker 自身是无法使用管理员身份启动的**，除非自己在 Windows 里设置兼容模式强制。"

> **开机自启**（getquicker.net/QA/34495）：用**计划任务（登录时）**启动，普通权限；官方**不推荐**管理员运行 Quicker。

**结论：Quicker 不用 `requireAdministrator`，而是 `asInvoker + UIAccess`。**

---

## 三、UIAccess 原理与硬前提

### 3.1 原理
- `uiAccess="true"` 让程序以**中完整性 + UIAccess 特权**运行：可**绕过 UIPI**，与更高完整性级别的窗口交互 UI。
- 对本项目的直接价值：
  - 全局低层钩子（`WH_KEYBOARD_LL`/`WH_MOUSE_LL`）**能收到**管理员窗口（yuzu/任务管理器）内的键鼠输入 → 可唤起；
  - `SendInput` 合成输入**能送达**管理员窗口 → 磁贴动作生效。
- 相比 `requireAdministrator`：**不弹 UAC、不是完全管理员、无 1.2 的副作用**，但足以操作管理员窗口的 UI。

### 3.2 硬前提（必须同时满足，否则 `uiAccess="true"` 被系统忽略、退回普通）
1. **可执行文件必须数字签名**：证书链须连到本机"**受信任的根证书颁发机构**"，且发布者受信任。
2. **必须安装在"安全位置"**：`%ProgramFiles%`（或 `Program Files (x86)`、`Windows\System32`）下。
   - 开发期的 `bin\Debug` / E 盘路径**不满足** → 需改为安装到 `C:\Program Files\QuickLauncher\`。

---

## 四、目标形态

| 维度 | 现状（第 1 步） | 目标（第 2 步） |
|---|---|---|
| manifest 权限 | `requireAdministrator` | `asInvoker` + `uiAccess="true"` |
| 启动 UAC | 每次弹 | **不弹** |
| 安装位置 | `bin\Debug`（E 盘） | `C:\Program Files\QuickLauncher\` |
| 代码签名 | 无 | **必须**（自签名用于本机；OV/EV 用于分发） |
| 开机自启 | 计划任务·登录时·**最高权限** | 计划任务·登录时·**普通权限(LIMITED)** |
| 子进程权限 | 全继承管理员 | 普通权限 |
| 在 yuzu 内 | 可用 | 可用（靠 UIAccess） |

---

## 五、实施步骤（可执行清单）

1. **改 manifest**：`src/QuickLauncher.App/app.manifest`
   - `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`
     → `<requestedExecutionLevel level="asInvoker" uiAccess="true" />`
   - 保留 PerMonitorV2 DPI 设置。
2. **准备代码签名**
   - 本机测试：`New-SelfSignature` 生成 Code Signing 自签名证书 → 导出 `.pfx` → 导入到 **受信任的根** + **受信任的发布者** → `signtool sign` 签名主 exe。
   - 分发：改用受信任 CA 的 OV/EV 代码签名证书（否则用户机不信任、UIAccess 失效）。
3. **接入安装/部署流程**
   - 目标目录：`C:\Program Files\QuickLauncher\`。
   - 提供部署脚本 `deploy.ps1`：`dotnet build -c Release` → 停止旧实例 → 复制输出到 Program Files → `signtool` 签名。
   - 数据目录仍为 `%LOCALAPPDATA%\QuickLauncher`（与安装位置解耦，普通权限可写，无需随安装提权）。
4. **调整 `StartupService`**
   - 计划任务 `/RL HIGHEST` → `/RL LIMITED`（`asInvoker` 不再需要最高权限；UIAccess 负责高权限窗口交互）。
   - `ExePath` 用安装后的 `Environment.ProcessPath`（装在 Program Files 后自动指向正确路径）。
   - （可选，照抄 Quicker）用一小段 VBS 以隐藏窗口方式启动，避免登录时闪控制台/窗口。
5. **移除/归档旧的 `requireAdministrator` 相关说明**（README"已知限制"、注释），更新文档。
6. **构建 → 签名 → 部署到 Program Files → 以普通方式启动验证**。

---

## 六、代码改动点（预期）

| 文件 | 改动 |
|---|---|
| `src/QuickLauncher.App/app.manifest` | `level="asInvoker" uiAccess="true"` |
| `src/QuickLauncher.App/Services/StartupService.cs` | 计划任务 `/RL` 改 `LIMITED`；`ExePath` 用安装路径；（可选）VBS 启动 |
| `src/QuickLauncher.App/QuickLauncher.App.csproj` | 已引用 `app.manifest`，无需改；如需 Release 配置/输出路径再调整 |
| `deploy.ps1`（新增，仓库根或 scripts/） | build + 复制到 Program Files + signtool 签名 |
| `README.md` / `docs/*` | 更新启动/自启/权限说明 |

> 说明：UIAccess 只解决 **UI 输入**层面的高权限窗口交互。本项目的目标场景（在 yuzu 里唤起面板 + 发键鼠）已覆盖；如未来有"写 HKLM"等非 UI 的管理员需求，仍按需对**单个动作**用 `runas` 提权（与整体提权解耦）。

---

## 七、签名与安装（关键命令参考）

### 7.1 自签名证书（仅本机测试）
```powershell
# 生成 Code Signing 自签名证书
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=QuickLauncher Dev" `
    -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature -KeyLength 2048
$pwd = ConvertTo-SecureString -String $env:QL_PFX_PASSWORD -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$env:USERPROFILE\ql-dev.pfx" -Password $pwd
# 导入到“受信任的根”和“受信任的发布者”（本机测试必需）
Import-Certificate -FilePath "$env:USERPROFILE\ql-dev.pfx" -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath "$env:USERPROFILE\ql-dev.pfx" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```
> 自签名证书**只在本机有效**；换机器/分发必须用受信任 CA 的 OV/EV 证书。

### 7.2 签名 exe
```powershell
signtool sign /f "$env:USERPROFILE\ql-dev.pfx" /p $env:QL_PFX_PASSWORD /fd SHA256 `
    "C:\Program Files\QuickLauncher\QuickLauncher.App.exe"
# 正式证书建议加时间戳：/tr http://timestamp.digicert.com /td SHA256
```

### 7.3 部署脚本（deploy.ps1 伪代码）
```powershell
dotnet build "QuickLauncher.sln" -c Release
$dest = "$env:ProgramFiles\QuickLauncher"
Stop-Process -Name QuickLauncher.App -Force -ErrorAction SilentlyContinue   # 需管理员结束旧实例
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "src\QuickLauncher.App\bin\Release\net7.0-windows\*" $dest -Recurse -Force
signtool sign /f "$env:USERPROFILE\ql-dev.pfx" /p $env:QL_PFX_PASSWORD /fd SHA256 "$dest\QuickLauncher.App.exe"
```

### 7.4 计划任务（asInvoker 普通权限）
```
schtasks /Create /TN "QuickLauncher" /TR "\"C:\Program Files\QuickLauncher\QuickLauncher.App.exe\"" /SC ONLOGON /RL LIMITED /F
```

---

## 八、验证清单

- [ ] 在 **yuzu（管理员）**内：鼠标停在 yuzu 窗口按侧键 X2 **能唤起**面板；点磁贴动作（`SendInput`）**对 yuzu 生效**。
- [ ] 在 **任务管理器（管理员）** 内可正常唤起/操作（UIAccess 的标志能力）。
- [ ] **启动不再弹 UAC**（普通双击/计划任务登录启动均不弹）。
- [ ] **开机自启生效**：注销重登或重启后，QuickLauncher 自动以普通+UIAccess 身份启动。
- [ ] **副作用回归**：`QuickLauncher.App.exe --run <actionId>` 能执行；通过 QuickLauncher 启动的子进程为**普通权限**（不再全管理员）。
- [ ] 完整性级别核验：QuickLauncher = 中完整性（`S-1-16-8192`）但具备 UIAccess；yuzu 内输入可捕获。
- [ ] `%LOCALAPPDATA%\QuickLauncher` 数据读写正常（安装到 Program Files 后不受影响）。

---

## 九、风险与注意事项

1. **签名是硬门槛**：未签名或不受信任 → `uiAccess=true` 失效，退回普通权限，yuzu 内又无法唤起。**本机测试**务必把自签名证书装入"受信任的根"+"受信任的发布者"。
2. **安装位置是硬门槛**：必须位于 Program Files 等安全位置；`bin\Debug`/E 盘无效。开发期调试会变麻烦，靠 `deploy.ps1` 缓解（build→部署→签名→运行）。
3. **开发流程变重**：每次改动需"构建+签名+复制到 Program Files"才能验证 UIAccess 行为；建议平时仍可跑 bin 调试非权限相关功能，权限相关验证走 deploy。
4. **分发考量**：正式分发需 OV/EV 证书，否则用户机器 UIAccess 不生效（会退回普通权限）。
5. **Program Files 写入需管理员**：仅安装/更新时需要；运行期数据写 `%LOCALAPPDATA%`（普通可写），不影响。
6. **UIAccess ≠ 完全管理员**：仅覆盖 UI 输入交互；非 UI 的管理员操作仍需按需单独提权（本项目当前无此需求）。

---

## 十、回滚方案

如需退回第 1 步状态：
1. `app.manifest` 改回 `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`。
2. `StartupService` 计划任务 `/RL` 改回 `HIGHEST`。
3. 重新 build、部署（可回到 bin 或 Program Files）。
4. 删除/替换自签名证书（如需）。
5. 数据目录 `%LOCALAPPDATA%\QuickLauncher` 不变，无需迁移。

---

## 十一、待决策点（执行前确认）

1. **证书来源**：本机测试用自签名；是否现在就购买 OV/EV 用于分发？（影响第 7.1/7.2）
2. **开发期是否安装到 Program Files**：是每次 debug 都走 deploy，还是仅发布/验证 UIAccess 时才部署？
3. **计划任务权限**：确认用 `/RL LIMITED`（普通+UIAccess）而非 HIGHEST？
4. **是否照抄 VBS 隐藏窗启动**（避免登录时闪窗）？
5. **安装器形态**：是否引入正式安装包（MSIX/Inno Setup 等），还是继续用 `deploy.ps1` 手工部署？

---

---

## 附：本次执行进度记录

**已完成（普通权限即可）**
- 核实代码改动到位：`app.manifest`(asInvoker+uiAccess)、`StartupService`(/RL LIMITED)、csproj 引用 manifest；并修正 csproj 中过时的“以管理员身份运行”注释。
- `dotnet build -c Release` 通过（0 错误，仅 null 性/DPI 警告）。
- 确认 `%USERPROFILE%\ql-dev.pfx` 有效（CN=QuickLauncher Dev，自带私钥，NotAfter 2027-10-29，Thumb 7AC2EB2F…48C1）。
- 确认本机三个证书存储（CurrentUser\My、LocalMachine\Root、LocalMachine\TrustedPublisher）目前**均无**该证书 → UIAccess 硬前提未满足。
- 修复脚本：补 UTF-8 BOM；`deploy.ps1` 自动定位 signtool（`…\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe`）；`setup-signing.ps1` 去掉不兼容的 `-CodeSigningCert`；新增 `scripts/bootstrap.ps1`。三脚本语法校验通过。

**待管理员执行（本会话非管理员，无法代做）**
1. 以**管理员**打开 PowerShell，`cd` 到仓库根目录，运行：
   `powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1`
   （内部依次：导入证书到受信任根/发布者 → 构建 → 复制到 `C:\Program Files\QuickLauncher` → 签名并 `signtool verify /pa`）
2. 运行后按第八节清单验收（重点：yuzu / 任务管理器内唤起 + SendInput 生效；启动不弹 UAC；开机自启生效）。

> 说明：`StartupService` 在应用启动时会用 `/Create /F` 覆盖计划任务为 `/RL LIMITED`；`ExePath` 取 `Environment.ProcessPath`，部署到 Program Files 后自动指向正确路径。

---

*文档创建于第 1 步完成后，作为第 2 步执行蓝图；执行时按第五、七节步骤推进，并按第八节清单验收。*
