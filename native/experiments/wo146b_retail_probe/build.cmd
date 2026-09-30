@echo off
rem WO-146B research probe -- one-shot build, no CMake.
rem
rem   build.cmd <n>      ->  native\build\wo146b\wo146b_probe_v<n>.dll
rem
rem Each rebuild MUST get a new file name: a second LoadLibrary of the same
rem base name is a no-op in a process that already has it, and a second probe
rem cannot re-patch a prologue the first one already replaced (WO-116).
setlocal
if "%~1"=="" (echo usage: build.cmd ^<version-number^> & exit /b 2)
set VER=%~1
set HERE=%~dp0
set OUT=%HERE%..\..\build\wo146b
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OUT%\obj%VER%" mkdir "%OUT%\obj%VER%"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 (echo vcvars64 failed & exit /b 1)
pushd "%OUT%"
cl /nologo /LD /O2 /EHsc /MT /std:c++17 /W3 ^
   /Fe:wo146b_probe_v%VER%.dll ^
   /Fo:obj%VER%\ ^
   "%HERE%probe.cpp" "%HERE%probe_pe.cpp" "%HERE%probe_hook.cpp" ^
   /link /PDBALTPATH:%%_PDB%% bcrypt.lib
set RC=%ERRORLEVEL%
popd
if %RC% NEQ 0 (echo BUILD FAILED %RC% & exit /b %RC%)
echo BUILT %OUT%\wo146b_probe_v%VER%.dll
exit /b 0
