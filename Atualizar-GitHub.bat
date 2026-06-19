@echo off
setlocal
chcp 65001 >nul

cd /d "%~dp0"

echo.
echo ==========================================
echo   Atualizar Ludryn no GitHub
echo ==========================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo Git nao encontrado. Instale o Git para Windows antes de continuar.
    pause
    exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo Esta pasta nao parece ser um repositorio Git.
    pause
    exit /b 1
)

for /f "tokens=*" %%b in ('git branch --show-current') do set "CURRENT_BRANCH=%%b"
if "%CURRENT_BRANCH%"=="" (
    echo Nao foi possivel detectar a branch atual.
    pause
    exit /b 1
)

echo Branch atual: %CURRENT_BRANCH%
echo.
echo Alteracoes encontradas:
git status --short
echo.

git diff --quiet && git diff --cached --quiet
if not errorlevel 1 (
    echo Nao ha alteracoes para enviar.
    pause
    exit /b 0
)

set "COMMIT_MESSAGE="
set /p COMMIT_MESSAGE=Mensagem do commit: 
if "%COMMIT_MESSAGE%"=="" (
    set "COMMIT_MESSAGE=Atualiza Ludryn"
)

echo.
echo O script vai executar:
echo   git add -A
echo   git commit -m "%COMMIT_MESSAGE%"
echo   git push origin %CURRENT_BRANCH%
echo.
choice /c SN /m "Confirmar envio para o GitHub"
if errorlevel 2 (
    echo Operacao cancelada.
    pause
    exit /b 0
)

echo.
echo Preparando arquivos...
git add -A
if errorlevel 1 goto :erro

echo.
echo Criando commit...
git commit -m "%COMMIT_MESSAGE%"
if errorlevel 1 goto :erro

echo.
echo Enviando para o GitHub...
git push origin %CURRENT_BRANCH%
if errorlevel 1 goto :erro

echo.
echo Pronto. O GitHub foi atualizado com sucesso.
pause
exit /b 0

:erro
echo.
echo Ocorreu um erro. Confira a mensagem acima antes de tentar novamente.
pause
exit /b 1
