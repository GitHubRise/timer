[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$iconPath = Join-Path $root 'assets\GameClock.ico'
$manifestPath = Join-Path $root 'build\app.manifest'
$configPath = Join-Path $root 'build\GameClock.exe.config'
$exePath = Join-Path $distDir 'GameClock.exe'
$zipPath = Join-Path $distDir 'GameClock-v0.0.1.zip'
$checksumPath = Join-Path $distDir 'SHA256SUMS.txt'
$buildNumberPath = Join-Path $root 'build\build.number'
$generatedDir = Join-Path $sourceDir 'generated'
$buildInfoPath = Join-Path $generatedDir 'BuildInfo.g.cs'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = '0.0.1'

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
if (-not (Test-Path -LiteralPath $generatedDir)) {
    New-Item -ItemType Directory -Path $generatedDir | Out-Null
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

$sources = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { $_.FullName })
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

$nativeSource = Join-Path $root 'native\taskbar\GameClockTray.cpp'
$nativeDll = Join-Path $distDir 'GameClockTray.dll'
$vcvars = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsPath) {
        $candidate = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
        if (Test-Path -LiteralPath $candidate) {
            $vcvars = $candidate
        }
    }
}
if (-not $vcvars) {
    $known = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat'
    )
    foreach ($candidate in $known) {
        if (Test-Path -LiteralPath $candidate) {
            $vcvars = $candidate
            break
        }
    }
}
if ($vcvars) {
    $objDir = Join-Path $root 'build\obj'
    if (-not (Test-Path -LiteralPath $objDir)) {
        New-Item -ItemType Directory -Path $objDir | Out-Null
    }
    $objFile = Join-Path $objDir 'GameClockTray.obj'
    Write-Host '正在编译 GameClockTray.dll（任务栏绘制组件）...'
    $nativeCommand = 'call "' + $vcvars + '" >nul && cl /nologo /LD /MT /O2 /DUNICODE /D_UNICODE /EHsc "' +
        $nativeSource + '" /Fo:"' + $objFile + '" /Fe:"' + $nativeDll +
        '" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib uxtheme.lib'
    & cmd.exe /c $nativeCommand
    if ($LASTEXITCODE -ne 0) {
        throw "原生任务栏组件编译失败，退出码：$LASTEXITCODE"
    }
} else {
    Write-Warning '未找到 Visual Studio C++ 工具链，跳过 GameClockTray.dll；任务栏时钟将不可用。'
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
$legacyZips = @('GameClock-v1.0.zip', 'GameClock-v1.1.zip')
foreach ($legacyName in $legacyZips) {
    $legacyZip = Join-Path $distDir $legacyName
    if (Test-Path -LiteralPath $legacyZip) {
        Remove-Item -LiteralPath $legacyZip -Force
    }
}
$packageFiles = @(
    (Join-Path $distDir 'GameClock.exe'),
    (Join-Path $distDir 'GameClock.exe.config'),
    (Join-Path $distDir 'README.md')
)
if (Test-Path -LiteralPath $nativeDll) {
    $packageFiles += $nativeDll
}
Compress-Archive -Path $packageFiles -DestinationPath $zipPath -Force

$checksumFiles = @(
    'GameClock.exe',
    'GameClock.exe.config',
    'README.md',
    'GameClock-v0.0.1.zip'
)
if (Test-Path -LiteralPath $nativeDll) {
    $checksumFiles += 'GameClockTray.dll'
}
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
