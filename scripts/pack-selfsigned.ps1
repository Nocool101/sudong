# pack-selfsigned.ps1 — 自签名版一键打包：publish → 签名 → 导 cer → 编译 Inno 安装包
#
# 与 scripts\sign-and-pack.ps1 的分工：
#   sign-and-pack.ps1  面向【正式 OV/EV 证书】——公共信任，任何机器都认，
#                       故编译 sudong-ov.iss（自动去掉 certutil 导入证书步骤）。
#   本脚本             面向【本机自签名证书】——只在本机受信任，
#                       故编译 build\sudong.iss（保留 certutil 把证书导入
#                       「受信任的根」+「受信任的发布者」，这是 UIAccess 硬前提之一）。
#
# 前置条件（【管理员】PowerShell 执行一次；忘记 pfx 密码时就靠它重设）：
#   $env:QL_PFX_PASSWORD = "你的新密码"
#   powershell -ExecutionPolicy Bypass -File scripts\setup-signing.ps1
#   （会生成自签名证书 → 导出 pfx → 导入受信任根/发布者）
#
# 然后（普通权限即可）运行本脚本：
#   $env:QL_PFX_PASSWORD = "同一个密码"
#   powershell -ExecutionPolicy Bypass -File scripts\pack-selfsigned.ps1
#
# 产物：发布版\v1.0.0\速动-Setup-1.0.0.exe
#   安装后自动完成 UIAccess 三前提（签名 + Program Files + 证书入信任存储）
#   与开机自启（注册表 HKCU Run 项），用户无需任何额外操作。

param(
    [string]$PfxPath      = "$env:USERPROFILE\ql-dev.pfx",
    [string]$PfxPassword  = $(if ($env:QL_PFX_PASSWORD) { $env:QL_PFX_PASSWORD } else { "" }),
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($PfxPassword) -and -not $SkipSign) {
    throw "未提供 pfx 密码：请设 `$env:QL_PFX_PASSWORD，或用 -PfxPassword 指定。忘记密码就见文件头注释，用 setup-signing.ps1 重设。"
}
if (-not (Test-Path $PfxPath) -and -not $SkipSign) { throw "找不到证书文件: $PfxPath" }

$root = Split-Path $PSScriptRoot
$proj  = Join-Path $root "src\QuickLauncher.App\QuickLauncher.App.csproj"
$pub   = Join-Path $root "build\publish"
$iss   = Join-Path $root "build\sudong.iss"

# ---- 定位 signtool.exe：优先 PATH，其次 Windows Kits 常见路径 ----
function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kitRoots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
    $found = @()
    foreach ($r in $kitRoots) {
        if (Test-Path $r) {
            $found += Get-ChildItem -Path $r -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                      Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' }
        }
    }
    if ($found.Count -gt 0) { return ($found | Sort-Object FullName | Select-Object -Last 1).FullName }
    return $null
}
$signtool = Find-SignTool

$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "未找到 ISCC.exe，请先安装 Inno Setup 6。" }

Write-Host "==> [1/5] 发布（自包含单文件 win-x64）" -ForegroundColor Cyan
# 必须 publish：dotnet build 不会嵌入 app.manifest 的 requestedExecutionLevel，
# 会用 SDK 默认的 asInvoker/uiAccess=false，导致 UIAccess 静默失效。
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:UIAccessManifest=true `
    -o $pub -v minimal
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

$exe = Join-Path $pub "QuickLauncher.App.exe"

if ($SkipSign) {
    Write-Warning "==> [2/5] 已跳过签名（-SkipSign）。未签名时 UIAccess 将被系统忽略，管理员窗口内无法唤起。"
} else {
    Write-Host "==> [2/5] 签名（含时间戳）: $signtool" -ForegroundColor Cyan
    & $signtool sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr $TimestampUrl /td sha256 /d "QuickLauncher" $exe
    if ($LASTEXITCODE -ne 0) { throw "签名失败（signtool 返回 $LASTEXITCODE）——请检查 pfx 密码是否正确" }
    & $signtool verify /pa $exe
    if ($LASTEXITCODE -ne 0) { throw "签名校验失败" }

    Write-Host "==> [3/5] 导出证书公钥（随安装包分发，供 certutil 导入信任存储）" -ForegroundColor Cyan
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
    [System.IO.File]::WriteAllBytes(
        (Join-Path $pub "ql-dev.cer"),
        $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Write-Host "    证书: $($cert.Subject)  Thumbprint=$($cert.Thumbprint)"
}

Write-Host "==> [4/5] 清理调试符号" -ForegroundColor Cyan
Remove-Item (Join-Path $pub "*.pdb") -Force -ErrorAction SilentlyContinue

Write-Host "==> [5/5] 编译 Inno 安装包（build\sudong.iss，保留 certutil 导入证书）" -ForegroundColor Cyan
$outDir = Join-Path $root "发布版\v1.0.0"
$setupExe = Join-Path $outDir "速动-Setup-1.0.0.exe"
# 编译会覆盖旧安装包，先备份，避免误覆盖后无法回退
if (Test-Path $setupExe) {
    Copy-Item $setupExe "$setupExe.bak" -Force
    Write-Host "    已备份旧安装包 -> 速动-Setup-1.0.0.exe.bak"
}
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "安装包编译失败（ISCC 返回 $LASTEXITCODE）" }

Write-Host ""
Write-Host "✅ 打包完成：$setupExe" -ForegroundColor Green
Write-Host ""
Write-Host "安装后自动完成（用户零操作）：" -ForegroundColor Yellow
Write-Host "  · 证书导入「受信任的根」+「受信任的发布者」（UIAccess 硬前提）"
Write-Host "  · 安装到 $env:ProgramFiles\QuickLauncher（UIAccess 硬前提：安全位置）"
Write-Host "  · 注册表 HKCU Run 项开机自启（由 explorer.exe 中完整性启动）"
Write-Host ""
Write-Host "⚠️ 自签名证书只在本机受信任。分发给其他电脑请改用 OV/EV 证书 + sign-and-pack.ps1。" -ForegroundColor Yellow
Write-Host "   验收重点：在 yuzu / 任务管理器（管理员窗口）内能否唤起面板并让 SendInput 生效。"
