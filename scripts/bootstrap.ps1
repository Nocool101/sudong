# bootstrap.ps1 — 一键完成第 2 步 UIAccess 改造的「证书 + 部署 + 签名」
# 依次执行：
#   1) setup-signing.ps1  生成/复用自签名证书，导入「受信任的根」+「受信任的发布者」
#   2) deploy.ps1         Release 构建 → 停止旧实例 → 复制到 Program Files → signtool 签名
#
# UIAccess 硬前提：exe 必须「已签名 + 位于 Program Files 等安全位置」，且证书链连到本机「受信任的根」。
# 故本脚本需以【管理员】身份运行（写受信任存储、写 Program Files、签名均需管理员权限）。
#
# 用法（仓库根目录，【管理员】PowerShell）：
#   powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1
# 非管理员启动时，本脚本会自动请求 UAC 提权重启自身。

$ErrorActionPreference = "Stop"

function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object System.Security.Principal.WindowsPrincipal($id)).
        IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-Admin)) {
    Write-Warning "需要【管理员】权限。正在请求提权并重新运行本脚本…"
    Start-Process powershell -Verb RunAs `
        -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File","`"$PSCommandPath`""
    exit 2
}

$here = $PSScriptRoot

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  第 2 步 · UIAccess 改造：证书 + 部署 + 签名"          -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

Write-Host "`n>>> 步骤 1/2：配置代码签名证书（setup-signing.ps1）`n" -ForegroundColor Yellow
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "setup-signing.ps1")
if ($LASTEXITCODE -ne 0) { throw "证书配置失败（setup-signing.ps1 返回 $LASTEXITCODE）" }

Write-Host "`n>>> 步骤 2/2：构建 + 部署到 Program Files + 签名（deploy.ps1）`n" -ForegroundColor Yellow
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "deploy.ps1")
if ($LASTEXITCODE -ne 0) { throw "部署失败（deploy.ps1 返回 $LASTEXITCODE）" }

Write-Host "`n✅ 全部完成。接下来按 docs\第2步-UIAccess改造计划.md 第八节清单验收：" -ForegroundColor Green
Write-Host "   普通方式启动（应不再弹 UAC）：& `"$env:ProgramFiles\QuickLauncher\QuickLauncher.App.exe`""
Write-Host "   命令行执行动作：& `"$env:ProgramFiles\QuickLauncher\QuickLauncher.App.exe`" --run <actionId>"
Write-Host "   重点验证：在 yuzu / 任务管理器（管理员窗口）内能否唤起并让 SendInput 生效。"
