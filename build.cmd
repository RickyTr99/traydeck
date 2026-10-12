@echo off
rem Builds bin\TrayDeck-Setup.exe: a single executable that is both the installer and the app.
rem Uses the C# compiler that ships with .NET Framework 4: no Visual Studio needed.
setlocal
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist bin mkdir bin

%CSC% -nologo -target:winexe -optimize -win32icon:assets\app.ico -win32manifest:assets\app.manifest ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Management.dll ^
  -resource:assets\app.ico,app.ico ^
  -out:bin\TrayDeck-Setup.exe src\Entry.cs src\TrayDeck.cs src\Setup.cs src\Shared.cs || exit /b 1

echo Built bin\TrayDeck-Setup.exe