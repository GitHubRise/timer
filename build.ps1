[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$iconPath = Join-Path $root 'assets\GameClock.ico'
$manifestPath = Join-Path $root 'app.manifest'
$configPath = Join-Path $root 'GameClock.exe.config'
$exePath = Join-Path $distDir 'GameClock.exe'
$zipPath = Join-Path $distDir 'GameClock-v1.1.zip'
$checksumPath = Join-Path $distDir 'SHA256SUMS.txt'
$buildNumberPath = Join-Path $root 'build.number'
$buildInfoPath = Join-Path $sourceDir 'BuildInfo.g.cs'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = '1.1.0'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到 .NET Framework 4.x 编译器：$csc"
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    & (Join-Path $root 'tools\make-icon.ps1')
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "缺少清单文件：$manifestPath"
}

if ($Clean -and (Test-Path -LiteralPath $distDir)) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$buildCounter = 0
if (Test-Path -LiteralPath $buildNumberPath) {
    $existingCounter = 0
    if ([int]::TryParse((Get-Content -LiteralPath $buildNumberPath -Raw).Trim(), [ref]$existingCounter)) {
        $buildCounter = $existingCounter
    }
}
$buildCounter++
[System.IO.File]::WriteAllText(
    $buildNumberPath,
    [string]$buildCounter,
    (New-Object System.Text.UTF8Encoding($false)))
$buildMoment = Get-Date
$buildNumber = $buildMoment.ToString('yyyyMMdd') + '.' + $buildCounter
$buildDateText = $buildMoment.ToString('yyyy-MM-dd HH:mm:ss')
$buildInfo = @"
namespace GameClock
{
    internal static class BuildInfo
    {
        public const string ProductName = "桌面时间悬浮窗";
        public const string Version = "$version";
        public const string BuildNumber = "$buildNumber";
        public const string BuildDate = "$buildDateText";
    }
}
"@
[System.IO.File]::WriteAllText($buildInfoPath, $buildInfo, (New-Object System.Text.UTF8Encoding($true)))
Write-Host ("版本：{0}  编译号：{1}" -f $version, $buildNumber)

$frameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpfDir = Join-Path $frameworkDir 'WPF'
$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Join-Path $frameworkDir 'System.Drawing.dll'),
    (Join-Path $frameworkDir 'System.Runtime.Serialization.dll'),
    (Join-Path $frameworkDir 'System.Windows.Forms.dll'),
    (Join-Path $frameworkDir 'System.Xaml.dll'),
    (Join-Path $frameworkDir 'System.Xml.dll'),
    (Join-Path $wpfDir 'WindowsBase.dll'),
    (Join-Path $wpfDir 'PresentationCore.dll'),
    (Join-Path $wpfDir 'PresentationFramework.dll'),
    (Join-Path $wpfDir 'UIAutomationTypes.dll')
)

$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw "src 目录中没有 .cs 源文件。"
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/warn:4',
    '/utf8output',
    '/codepage:65001',
    ('/out:' + $exePath),
    ('/win32icon:' + $iconPath),
    ('/win32manifest:' + $manifestPath)
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "缺少引用程序集：$reference"
    }
    $arguments += ('/r:' + $reference)
}

Write-Host ("正在编译 GameClock.exe（{0} 个源文件）..." -f $sources.Count)
$fullArguments = @($arguments)
$fullArguments += [string[]]$sources
& $csc $fullArguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

Copy-Item -LiteralPath $configPath -Destination (Join-Path $distDir 'GameClock.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $distDir 'README.md') -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
$legacyZip = Join-Path $distDir 'GameClock-v1.0.zip'
if (Test-Path -LiteralPath $legacyZip) {
    Remove-Item -LiteralPath $legacyZip -Force
}
Compress-Archive -Path (Join-Path $distDir 'GameClock.exe'), (Join-Path $distDir 'GameClock.exe.config'), (Join-Path $distDir 'README.md') -DestinationPath $zipPath -Force

$checksumFiles = @(
    'GameClock.exe',
    'GameClock.exe.config',
    'README.md',
    'GameClock-v1.1.zip'
)
$checksumLines = @()
foreach ($name in $checksumFiles) {
    $filePath = Join-Path $distDir $name
    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines += ($hash + '  ' + $name)
}
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksumLines, (New-Object System.Text.UTF8Encoding($false)))

$item = Get-Item -LiteralPath $exePath
Write-Host ("编译完成：{0} ({1:N0} 字节)" -f $item.FullName, $item.Length)
Write-Host ("发布包：{0}" -f $zipPath)
Write-Host ("SHA-256 校验文件：{0}" -f $checksumPath)
