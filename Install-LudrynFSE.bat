@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

title Ludryn Xbox Home App Installer
echo.
echo ============================================================
echo   Ludryn - Instalar como Xbox Home App
echo ============================================================
echo.
echo Este instalador vai:
echo   1. Compilar o pacote FSE do Ludryn.
echo   2. Criar e confiar o certificado local.
echo   3. Instalar o Ludryn como aplicativo Xbox Home.
echo.
echo O Windows pode solicitar permissao de administrador.
echo.

set "READY_INSTALLER="
for /f "delims=" %%F in ('dir /b /a-d /o-d "%~dp0Ludryn-Setup-*.exe" 2^>nul') do if not defined READY_INSTALLER set "READY_INSTALLER=%~dp0%%F"
for /f "delims=" %%F in ('dir /b /a-d /o-d "%~dp0artifacts\Release\Ludryn-Setup-*.exe" 2^>nul') do if not defined READY_INSTALLER set "READY_INSTALLER=%~dp0artifacts\Release\%%F"

if defined READY_INSTALLER (
    start "" /wait "!READY_INSTALLER!"
    exit /b !ERRORLEVEL!
)

echo Nenhum instalador pronto foi encontrado.
echo Gerando uma versao local para desenvolvimento...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-LudrynFse.ps1"
set "INSTALL_RESULT=%ERRORLEVEL%"

echo.
if not "%INSTALL_RESULT%"=="0" (
    echo A instalacao nao foi concluida.
    echo.
    echo Verifique se o Visual Studio esta instalado com:
    echo   - Desenvolvimento de aplicativos Windows
    echo   - Windows App SDK
    echo   - MSIX Packaging Tools
    echo.
    pause
    exit /b %INSTALL_RESULT%
)

echo Instalacao concluida.
echo.
echo Agora abra:
echo Configuracoes do Windows ^> Jogos ^> Experiencia de tela inteira
echo e escolha Ludryn como aplicativo inicial.
echo.
pause
exit /b 0
