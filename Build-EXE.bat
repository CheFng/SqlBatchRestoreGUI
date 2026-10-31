@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo   SQL Server BAK Batch Restore - EXE Builder
echo ============================================================
echo.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [ERROR] .NET Framework C# compiler was not found.
    echo Please enable/install .NET Framework 4.x, then run this file again.
    pause
    exit /b 1
)

if not exist "SqlBatchRestore.ico" (
    echo [ERROR] Icon file SqlBatchRestore.ico is missing.
    pause
    exit /b 1
)

if exist "SqlBatchRestore.exe" del /q "SqlBatchRestore.exe"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /win32icon:"SqlBatchRestore.ico" /out:"SqlBatchRestore.exe" ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Data.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 "Source\Program.cs"

if errorlevel 1 (
    echo.
    echo [FAILED] Compilation failed. Please copy the errors and send them back to ChatGPT.
    pause
    exit /b 1
)

echo.
echo [OK] Generated:
echo     %CD%\SqlBatchRestore.exe
echo.
echo You can now double-click SqlBatchRestore.exe.
pause
exit /b 0
