param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root "Ludryn\Package.appxmanifest"
$installerProject = Join-Path $root "Installer\Ludryn.Installer\Ludryn.Installer.csproj"
$payloadDirectory = Join-Path $root "Installer\Ludryn.Installer\Payload"
$releaseDirectory = Join-Path $root "artifacts\Release"
$publishDirectory = Join-Path $releaseDirectory "publish"

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$manifestVersion = $manifest.Package.Identity.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $manifestVersion
}

Write-Host "Gerando o pacote MSIX do Ludryn $Version..."
& (Join-Path $root "Install-LudrynFse.ps1") -BuildOnly
if ($LASTEXITCODE -ne 0) {
    throw "A geracao do MSIX falhou."
}

$package = Get-ChildItem (Join-Path $root "artifacts\FSE\Package") -Recurse -File |
    Where-Object {
        $_.Extension -eq ".msix" -and
        $_.FullName -notmatch "[\\/]Dependencies[\\/]" -and
        $_.BaseName -like "Ludryn_$Version*"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "O MSIX principal do Ludryn nao foi encontrado."
}

$runtime = Get-ChildItem $package.Directory -Recurse -File |
    Where-Object {
        $_.Name -eq "Microsoft.WindowsAppRuntime.2.msix" -and
        $_.FullName -match "[\\/]Dependencies[\\/]x64[\\/]"
    } |
    Select-Object -First 1

if (-not $runtime) {
    throw "A dependencia x64 do Windows App Runtime nao foi encontrada."
}

$certificate = Get-ChildItem (Join-Path $root "artifacts\FSE\Certificate") -Filter "Ludryn.cer" |
    Select-Object -First 1
if (-not $certificate) {
    throw "O certificado publico do Ludryn nao foi encontrado."
}

Remove-Item -LiteralPath $payloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $payloadDirectory, $publishDirectory | Out-Null

Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $payloadDirectory "Ludryn.msix")
Copy-Item -LiteralPath $runtime.FullName -Destination (Join-Path $payloadDirectory "WindowsAppRuntime.msix")
Copy-Item -LiteralPath $certificate.FullName -Destination (Join-Path $payloadDirectory "Ludryn.cer")

Write-Host "Gerando o instalador autocontido..."
dotnet publish $installerProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "A geracao do instalador falhou."
}

$publishedInstaller = Join-Path $publishDirectory "Ludryn-Setup.exe"
$finalInstaller = Join-Path $releaseDirectory "Ludryn-Setup-$Version.exe"
$latestInstaller = Join-Path $releaseDirectory "Ludryn-Setup.exe"
Copy-Item -LiteralPath $publishedInstaller -Destination $finalInstaller -Force

$signingCertificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq "CN=Ludryn" -and
        $_.HasPrivateKey
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

$signTool = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools") `
    -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "[\\/]x64[\\/]" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($signingCertificate -and $signTool) {
    Write-Host "Assinando o instalador..."
    & $signTool.FullName sign `
        /sha1 $signingCertificate.Thumbprint `
        /s My `
        /fd SHA256 `
        $finalInstaller

    if ($LASTEXITCODE -ne 0) {
        throw "A assinatura do instalador falhou."
    }
}
else {
    Write-Warning "Certificado ou SignTool nao encontrado. O instalador sera entregue sem assinatura."
}

$hash = Get-FileHash -LiteralPath $finalInstaller -Algorithm SHA256
$hashLine = "$($hash.Hash)  $([IO.Path]::GetFileName($finalInstaller))"
Set-Content -LiteralPath "$finalInstaller.sha256" -Value $hashLine -Encoding ascii

Copy-Item -LiteralPath $finalInstaller -Destination $latestInstaller -Force
$latestHash = Get-FileHash -LiteralPath $latestInstaller -Algorithm SHA256
$latestHashLine = "$($latestHash.Hash)  $([IO.Path]::GetFileName($latestInstaller))"
Set-Content -LiteralPath "$latestInstaller.sha256" -Value $latestHashLine -Encoding ascii

Write-Host ""
Write-Host "Release pronta:"
Write-Host $finalInstaller
Write-Host "O usuario final nao precisa do Visual Studio ou do .NET instalado."
