# بناء مثبّت منظومة ميزان
# 1) تشغيل الاختبارات  2) نشر نسخة Release إلى مجلد Publish  3) بناء المثبّت بـ Inno Setup
# الاستخدام (من أي مكان):  powershell -ExecutionPolicy Bypass -File Installer\build_installer.ps1
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'MeezanPOS\MeezanPOS.csproj'

# رقم الإصدار من المصدر الوحيد: <Version> في MeezanPOS.csproj
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'لم يُعثر على <Version> في MeezanPOS.csproj' }
Write-Host "إصدار المنظومة: $version"

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'MeezanPOS.Tests') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'فشلت الاختبارات؛ أُلغي بناء المثبّت.' }
}

dotnet publish $project -p:PublishProfile=Installer --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشل النشر.' }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'لم يُعثر على Inno Setup 6 (ISCC.exe). ثبّته من jrsoftware.org ثم أعد المحاولة.' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'MeezanPOS_Setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'فشل بناء المثبّت.' }

Write-Host "تم: Installer\Output\MeezanPOS_Setup_v$version.exe"
