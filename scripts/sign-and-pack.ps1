# sign-and-pack.ps1 — 用正式 OV/EV 代码签名证书重新签名并重打包安装包
# 适用：已从受信任 CA（DigiCert/Sectigo/GlobalSign/阿里云/腾讯云等）购得代码签名证书(.pfx,含私钥)，
#       并放入"证书"目录。OV/EV 为公共信任：签名的 exe 在任何电脑都被信任，
#       因此安装到其他机器 UIAccess 亦生效，安装包无需再"导入自签证书到受信任根"
#       (本脚本会自动生成去掉 certutil 步骤的 OV 版安装脚本)。
#
# 用法（普通权限即可，无需管理员）：
#   $env:QL_PFX_PASSWORD = "你的pfx密码"
#   powershell -ExecutionPolicy Bypass -File scripts\sign-and-pack.ps1 -PfxPath "E:\AI 工作目录\应用启动器证书\yourcert.pfx"
# 或：powershell -ExecutionPolicy Bypass -File scripts\sign-and-pack.ps1 -PfxPath "...\xxx.pfx" -PfxPassword "密码"

param(
    [Parameter(Mandatory=$true)][string]$PfxPath,
    [string]$PfxPassword = $(if ($env:QL_PFX_PASSWORD) { $env:QL_PFX_PASSWORD } else { "" }),
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($PfxPassword)) { throw "未提供证书密码：用 -PfxPassword 或环境变量 QL_PFX_PASSWORD。" }
if (-not (Test-Path $PfxPath)) { throw "证书文件不存在: $PfxPath" }

$root = Split-Path $PSScriptRoot
$issBase = Join-Path $root "build\sudong.iss"
if (-not (Test-Path $issBase)) { throw "未找到基础安装脚本: $issBase" }

$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
if ($null -eq $signtool) { throw "未找到 signtool.exe，请安装 Windows SDK。" }

$pub = Join-Path $root "build\publish\ql-dev.cer"

Write-Host "==> [1/4] 发布(自包含单文件 win-x64)"
$proj = Join-Path $root "src\QuickLauncher.App\QuickLauncher.App.csproj"
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "build\publish") -v minimal
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

Write-Host "==> [2/4] 用正式证书签名(含时间戳)"
$exe = Join-Path $root "build\publish\QuickLauncher.App.exe"
& $signtool.FullName sign /f $PfxPath /p $PfxPassword /fd SHA256 /tr $TimestampUrl /td sha256 /d "QuickLauncher" $exe
if ($LASTEXITCODE -ne 0) { throw "签名失败" }
& $signtool.FullName verify /pa $exe

Write-Host "==> [3/4] 导出证书公钥(随安装包分发/校验)"
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
[System.IO.File]::WriteAllBytes($pub, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

Write-Host "==> [4/4] 生成 OV 版安装脚本并编译"
$iss = Get-Content $issBase -Raw -Encoding UTF8
# 去掉"导入自签名证书"的 certutil 步骤（OV/EV 公共信任，无需本地导入）
$iss = [regex]::Replace($iss, '(?m)^.*certutil.*\r?\n', '')
$ovIss = Join-Path $root "build\sudong-ov.iss"
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($ovIss, $iss, $utf8Bom)
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "未找到 ISCC.exe，请先安装 Inno Setup。" }
& $iscc $ovIss
if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }

Write-Host ""
Write-Host ("DONE: " + (Join-Path $root "发布版\v1.0.0\速动-Setup-1.0.0.exe")) -ForegroundColor Green
