# Certum 开源代码签名证书（免费 EV）申请清单

> 适用场景：个人/小团队开源项目，零成本获得**公共信任的 EV 代码签名证书**，消除 Windows SmartScreen"未知发布者"误报。
> 本项目条件：MIT 许可证（OSI 认可）+ GitHub 公开仓库 `https://github.com/Nocool101/sudong`，**基本符合申请资格**。
> ⚠️ Certum 的开源证书政策（额度、有效期、验证方式）历史上多次调整，提交前务必以 <https://www.certum.eu> 官网当前页面为准。

---

## 一、申请资格核对（提交前逐条确认）

| # | 条件 | 本项目状态 |
|---|---|---|
| 1 | 项目源码公开可访问（GitHub/GitLab 公开仓库） | ✅ `Nocool101/sudong` 公开 |
| 2 | 使用 OSI 认可的开源许可证（MIT/Apache-2.0/GPL 等） | ✅ MIT |
| 3 | 仓库地址能证明归申请人所有（可添加文件/设置） | ✅ |
| 4 | 项目有真实开发迹象（commit 历史、release） | 按实际情况，建议先发一个 release 再申请 |
| 5 | 申请人有可用的邮箱（建议用常用邮箱，审核邮件勿漏） | — |
| 6 | 项目不是恶意软件/非"占位空仓库" | ✅ |

## 二、需要准备的材料/信息

- **项目名称**：速动 / QuickLauncher / sudong（保持与仓库一致）
- **项目一句话简介**：说明这是什么软件（例：Windows 快速启动工具）
- **仓库地址**：`https://github.com/Nocool101/sudong`
- **许可证类型**：MIT
- **申请人姓名**（个人申请）或公司名
- **联系邮箱**（能正常收信，审核通知发到这里）
- **国家/地区**
- **项目活跃度佐证**：release 链接、README 截图、commit 记录（有备无患）

## 三、申请步骤（certum.eu）

1. **注册账号**：<https://www.certum.eu> → 注册（邮箱激活）
2. **实名认证**：按引导完成账号认证（个人/企业按实际选）
3. **选择产品**：找 "Open Source Code Signing"（开源代码签名）免费产品入口；若导航里没有，直接搜 "open source" 或联系在线客服说明"我要申请开源项目的免费代码签名证书"
4. **填写项目信息表**：按第二节所列内容逐项填写
5. **仓库归属验证**：Certum 通常要求在仓库放置一个验证文件（如指定名称的 txt 或修改 README 加指定字符串），或要求从项目关联邮箱发起验证。按其邮件/页面提示操作
6. **等待审核**：一般数个工作日，留意邮箱（含垃圾箱）
7. **证书激活**：审核通过后按指引激活。开源证书通常以 **SimplySign 云签名**形式交付（证书在 Certum 云端 HSM，本地用其客户端工具签名，无需 Ukey 邮寄）；也可能要求使用其专用签名工具

## 四、常见卡点（提前规避）

| 卡点 | 规避方法 |
|---|---|
| 仓库太空、无 release | 先整理 README、打一个正式 release 再申请 |
| 验证邮件进垃圾箱 | 提交前把 certum.eu 加入邮箱白名单 |
| 产品入口找不到 | 直接找官网在线客服，关键词 "open source code signing certificate" |
| 仓库归属无法验证 | 确保申请邮箱与 GitHub 账号邮箱一致，能快速响应 Certum 的验证要求 |
| 公司/个人主体选错 | 个人开源选个人主体即可，无需营业执照 |

## 五、到手后的签名方式（两种）

证书交付形式决定了改造 `scripts/sign-and-pack.ps1` 的方式：

- **A. 给 `.pfx` 文件**（部分情况）：脚本原生支持，直接
  ```powershell
  $env:QL_PFX_PASSWORD = "密码"
  powershell -ExecutionPolicy Bypass -File scripts\sign-and-pack.ps1 -PfxPath "证书\yourcert.pfx"
  ```
- **B. 云签名/无 pfx**（Certum SimplySign 或 Ukey 的常见情况）：改用证书指纹选择本地/云证书
  ```powershell
  # 查看证书指纹（插好 Ukey 或登录云签名客户端后）
  Get-ChildItem Cert:\CurrentUser\My | Format-List Thumbprint, Subject
  # 签名
  & $signtool sign /sha1 <指纹> /fd SHA256 /tr <时间戳服务器> /td sha256 /d "QuickLauncher" <exe路径>
  ```
  此情况需要给 `sign-and-pack.ps1` 增加 `-Thumbprint` 参数（可再找我改）。

**时间戳服务器**：用 Certum 证书时换为其提供的 RFC 3161 时间戳地址（以 Certum 交付邮件为准），不要沿用脚本默认的 DigiCert 地址。

## 六、续期与维护

- 免费证书有效期有限（通常 1 年，以交付时说明为准），**设日历提醒提前 1 个月续期**
- 续期一般需重新走一次仓库验证（通常很快）
- 若项目长期停更，证书失效后旧签名仍有效（有时间戳的前提下）

---

*文档创建于 2025-10-30，申请前请再次核对官网最新政策。*
