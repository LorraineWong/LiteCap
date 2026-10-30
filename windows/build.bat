@echo off
rem Rebuild LiteCap.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0src"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:app.ico /resource:app.ico,app.ico /resource:logo.png,logo.png /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:..\LiteCap.exe *.cs
pause
