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

for /f "tokens=*" %%v in ('powershell -NoProfile -Command "([xml](Get-Content -Raw 'Ludryn\\Package.appxmanifest')).Package.Identity.Version"') do set "APP_VERSION=%%v"
if "%APP_VERSION%"=="" (
    set "APP_VERSION=sem-versao"
)

echo.
echo Registro do CHANGELOG.md
echo Versao detectada: %APP_VERSION%
set "CHANGELOG_ENTRY="
set /p CHANGELOG_ENTRY=O que mudou nesta versao: 
if "%CHANGELOG_ENTRY%"=="" (
    echo E necessario informar uma mensagem para o CHANGELOG.md.
    pause
    exit /b 1
)

set "LUDRYN_CHANGELOG_VERSION=%APP_VERSION%"
set "LUDRYN_CHANGELOG_ENTRY=%CHANGELOG_ENTRY%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$path='CHANGELOG.md'; $version=$env:LUDRYN_CHANGELOG_VERSION; $entry=$env:LUDRYN_CHANGELOG_ENTRY; $date=Get-Date -Format 'yyyy-MM-dd'; $content=''; if (Test-Path $path) { $content=Get-Content -LiteralPath $path -Raw -Encoding UTF8 }; $body=$content -replace '^\s*# Changelog\s*\r?\n\r?\n',''; if ($body -notmatch ('(?m)^##\s+' + [regex]::Escape($version) + '\b')) { $new = '# Changelog' + \"`r`n`r`n\" + '## ' + $version + ' - ' + $date + \"`r`n`r`n\" + '- ' + $entry + \"`r`n`r`n\" + $body.TrimStart(); Set-Content -LiteralPath $path -Value $new -Encoding UTF8 } else { $lines = $content -split \"`r?`n\"; $index = [Array]::FindIndex($lines, [Predicate[string]]{ param($line) $line -match ('^##\s+' + [regex]::Escape($version) + '\b') }); if ($index -ge 0) { $insert = $index + 2; $list = [System.Collections.Generic.List[string]]::new(); $list.AddRange([string[]]$lines); $list.Insert($insert, '- ' + $entry); Set-Content -LiteralPath $path -Value ($list -join \"`r`n\") -Encoding UTF8 } }"
if errorlevel 1 goto :erro

echo.
echo O script vai executar:
echo   atualizar CHANGELOG.md
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
