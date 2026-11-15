@echo off
rem Builds CapriCam.dll, the virtual camera's DirectShow filter. Needs Visual Studio's C++ tools.
setlocal
set VS=%ProgramFiles%\Microsoft Visual Studio\18\Community
if not exist "%VS%\VC\Auxiliary\Build\vcvars64.bat" (
  echo Visual Studio C++ tools not found. CapriCam.dll will not be rebuilt.
  exit /b 2
)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /LD /O2 /W3 /EHsc /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN ^
   capricam.cpp /Fe:%~dp0CapriCam.dll /link /DEF:%~dp0capricam.def ^
   strmiids.lib ole32.lib oleaut32.lib advapi32.lib user32.lib
exit /b %errorlevel%
