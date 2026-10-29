# deploy.ps1 — 速动(QuickLauncher) 部署脚本
# 构建 Release → 停止旧实例 → 部署到 Program Files → 代码签名。
# UIAccess 硬前提：可执行文件必须「已签名 + 位于 Program Files 等安全位置」。
# 因此本脚本需以【管理员】身份运行（写 Program Files、结束管理员旧实例、签名均需权限）。
#
# 用法（管理员 PowerShell）：
#   powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1
#   # 首次使用前，先配置证书：powershell -ExecutionPolicy Bypass -File scripts\setup-signing.ps1
#   # 仅部署不签名（不推荐，UIAccess 将不生效）：
#   powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1 -SkipSign

param(
    [string]$InstallDir  = "$env:ProgramFiles\QuickLauncher",
    [string]$PfxPath     = "$env:USERPROFILE\ql-dev.pfx",
    [string]$PfxPassword = $(if ($env:QL_PFX_PASSWORD) { $env:QL_PFX_PASSWORD } else { "" }),
    [string]$Project     = (Join-Path $PSScriptRoot "..\src\QuickLauncher.App\QuickLauncher.App.csproj"),
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"

# ---- 管理员自检：非管理员则尝试提权重启自身 ----
function Test-Admin {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object System.Security.Principal.WindowsPrincipal($id)).
        IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}
# ---- 定位 signtool.exe：优先 PATH，其次在 Windows Kits 常见路径搜索（本机 SDK 常不在 PATH）----
function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $found = @()
    $kitRoots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
    foreach ($root in $kitRoots) {
        if (Test-Path $root) {
            $found += Get-ChildItem -Path $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                      Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' }
        }
    }
    $ack = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\App Certification Kit\signtool.exe"
    if (Test-Path $ack) { $found += (Get-Item $ack) }
    if ($found.Count -gt 0) {
        return ($found | Sort-Object FullName | Select-Object -Last 1).FullName
    }
    return $null
}

if (-not (Test-Admin)) {
    Write-Warning "需要【管理员】权限。正在请求提权并重新运行本脚本…"
    Start-Process powershell -Verb RunAs `
        -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File","`"$PSCommandPath`""
    exit 2
}

$src = Join-Path $PSScriptRoot "..\src\QuickLauncher.App\bin\Release\net7.0-windows"

Write-Host "==> [1/4] 构建 Release" -ForegroundColor Cyan
dotnet build $Project -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败（dotnet build 返回 $LASTEXITCODE）" }

Write-Host "==> [2/4] 停止正在运行的旧实例" -ForegroundColor Cyan
Stop-Process -Name "QuickLauncher.App" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

Write-Host "==> [3/4] 部署到 $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $src "*") $InstallDir -Recurse -Force

if ($SkipSign) {
    Write-Warning "==> [4/4] 已跳过签名（-SkipSign）。未签名时 UIAccess 将被系统忽略。"
} else {
    Write-Host "==> [4/4] 代码签名" -ForegroundColor Cyan
    if (-not (Test-Path $PfxPath)) {
        throw "未找到证书 $PfxPath。请先以管理员运行：scripts\setup-signing.ps1"
    }
    $signtool = Find-SignTool
    if (-not $signtool) {
        throw "未找到 signtool.exe。请安装 Windows SDK（含 Signing Tools），或将其所在目录加入 PATH。"
    }
    Write-Host "    使用 signtool：$signtool"
    $exe = Join-Path $InstallDir "QuickLauncher.App.exe"
    if ([string]::IsNullOrEmpty($PfxPassword)) { throw "未提供 pfx 密码：请用 -PfxPassword 指定，或设置环境变量 QL_PFX_PASSWORD。" }
    & $signtool sign /f $PfxPath /p $PfxPassword /fd SHA256 $exe
    if ($LASTEXITCODE -ne 0) { throw "签名失败（signtool 返回 $LASTEXITCODE）" }

    # 显示签名信息，确认生效
    & $signtool verify /pa $exe
}

Write-Host ""
Write-Host "✅ 部署完成：$InstallDir\QuickLauncher.App.exe" -ForegroundColor Green
Write-Host "   启动（不再弹 UAC）：& '$InstallDir\QuickLauncher.App.exe'"
Write-Host "   命令行执行动作：& '$InstallDir\QuickLauncher.App.exe' --run <actionId>"
