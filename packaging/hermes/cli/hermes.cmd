@echo off
setlocal DisableDelayedExpansion
"%~dp0..\python\python.exe" -I -S -B "%~dp0entry.py" %*
exit /b %errorlevel%
