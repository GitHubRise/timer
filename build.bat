@echo off
rem Build entry for users who do not want to type a PowerShell command.
rem Usage: build.bat [-Clean]   (all arguments are forwarded to build.ps1)
setlocal
chcp 65001 >nul

set "PS1=%~dp0build.ps1"
if not exist "%PS1%" (
    echo [build] not found: %PS1%
    endlocal & exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "CODE=%ERRORLEVEL%"

if "%CODE%"=="0" (
    echo [build] OK
) else (
    echo [build] FAILED, exit code %CODE%
)
endlocal & exit /b %CODE%
