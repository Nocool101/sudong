# 安全政策 (Security Policy)

感谢你为「速动 (sudong)」的安全付出。

## 报告漏洞
如果你发现安全问题，请**不要**直接开公开 Issue。请通过以下方式私下报告：
- **推荐**：GitHub Security Advisory — https://github.com/Nocool101/sudong/security/advisories/new
- 或联系仓库维护者（nocool101）

我们会尽快确认并修复，感谢负责的披露（coordinated disclosure）。

## 支持范围
仅最新的 minor 版本会收到安全修复，建议始终使用最新 Release。

## 数据与隐私
- 本应用**完全本地运行**，无遥测、无统计上报、无网络请求（唯一联网行为是用户主动触发的「联网搜索」动作，由本地默认浏览器打开）。
- 所有数据仅存于本机 `%LOCALAPPDATA%\QuickLauncher`，**不上传任何服务器**。
- 详见 [PRIVACY.md](PRIVACY.md)。

## 已知安全说明
- 脚本动作（C#/JS/Python）会在你本机以你的用户权限执行，等同于你手动运行该脚本——请仅运行你信任的脚本。
- 应用安装全局键鼠钩子用于唤出面板（`WS_EX_NOACTIVATE`，不抢焦点）；以普通权限（`asInvoker`）运行，启动不弹 UAC。
