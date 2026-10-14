@echo off
setlocal
pwsh -NoProfile -File "%~dp0scripts\project.ps1" %*
exit /b %errorlevel%
