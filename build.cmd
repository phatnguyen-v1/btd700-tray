@echo off
rem Builds Btd700Tray.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize /out:Btd700Tray.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Program.cs HeadsetForm.cs HeadsetMode.cs Btd700.cs GameDetector.cs AssemblyInfo.cs
exit /b %ERRORLEVEL%
