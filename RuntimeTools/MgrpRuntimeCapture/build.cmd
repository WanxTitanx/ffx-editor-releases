@echo off
setlocal

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b %errorlevel%

if not exist build mkdir build

cl /nologo /EHsc /O2 /MT /LD MgrpCapturePassive.cpp /Febuild\MgrpCapturePassive.dll /link /NOLOGO
if errorlevel 1 exit /b %errorlevel%

cl /nologo /EHsc /O2 /MT MgrpInject.cpp /Febuild\MgrpInject.exe /link /NOLOGO
if errorlevel 1 exit /b %errorlevel%

echo Built build\MgrpCapturePassive.dll and build\MgrpInject.exe
