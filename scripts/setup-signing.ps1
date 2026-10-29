# setup-signing.ps1 — 配置本机自签名代码签名证书（仅供本机测试 UIAccess）
# 生成 Code Signing 自签名证书 → 导出 .pfx → 导入「受信任的根」+「受信任的发布者」。
# UIAccess 硬前提之一就是证书链连到本机「受信任的根证书颁发机构」，故必须导入这两个存储。
# 需以【管理员】身份运行（写入 LocalMachine\Root 与 LocalMachine\TrustedPublisher 需权限）。
#
# 用法（管理员 PowerShell，首次部署前执行一次）：
#   powershell -ExecutionPolicy Bypass -File scripts\setup-signing.ps1
#
# 说明：自签名证书只在本机有效。换机器/正式分发必须改用受信任 CA 的 OV/EV 代码签名证书，
#       并调整 deploy.ps1 的 -PfxPath/-PfxPassword 指向正式证书。

param(
    [string]$Subject     = "CN=QuickLauncher Dev",
    [string]$PfxPath     = "$env:USERPROFILE\ql-dev.pfx",
    [string]$PfxPassword = $(if ($env:QL_PFX_PASSWORD) { $env:QL_PFX_PASSWORD } else { "" })
)

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

Write-Host "==> 生成/复用作签名证书（$Subject）" -ForegroundColor Cyan
# 说明：部分系统的 PowerShell 证书提供程序不支持 Get-ChildItem 的 -CodeSigningCert 动态参数，
#       故改为按「Subject + 代码签名 EKU(1.3.6.1.5.5.7.3.3)」手动过滤，兼容性更好。
$CodeSigningEku = "1.3.6.1.5.5.7.3.3"
$cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $Subject -and ($_.EnhancedKeyUsageList.ObjectId -contains $CodeSigningEku) } |
        Select-Object -First 1
if ($null -eq $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature `
        -KeyLength 2048 `
        -HashAlgorithm SHA256
    Write-Host "    已创建新证书：$($cert.Thumbprint)"
} else {
    Write-Host "    复用已存在证书：$($cert.Thumbprint)"
}

Write-Host "==> 导出 .pfx 到 $PfxPath" -ForegroundColor Cyan
if ([string]::IsNullOrEmpty($PfxPassword)) { throw "未提供 pfx 密码：请用 -PfxPassword 指定，或设置环境变量 QL_PFX_PASSWORD。" }
$pwd = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $pwd | Out-Null

Write-Host "==> 导入「受信任的根证书颁发机构」" -ForegroundColor Cyan
Import-Certificate -FilePath $PfxPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

Write-Host "==> 导入「受信任的发布者」" -ForegroundColor Cyan
Import-Certificate -FilePath $PfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null

Write-Host ""
Write-Host "✅ 证书配置完成。" -ForegroundColor Green
Write-Host "   .pfx：$PfxPath"
Write-Host "   下一步（管理员 PowerShell）：powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1"
