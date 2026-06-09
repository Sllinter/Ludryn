param(
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildRoot = $root
$substDrive = $null

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $BuildOnly -and -not $isAdministrator) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    $process = Start-Process PowerShell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

if ($root -match "\s") {
    $usedDrives = Get-PSDrive -PSProvider FileSystem |
        ForEach-Object { $_.Name.ToUpperInvariant() }
    $driveLetter = [char[]](90..68) |
        ForEach-Object { [string][char]$_ } |
        Where-Object { $_ -notin $usedDrives } |
        Select-Object -First 1
    if (-not $driveLetter) {
        throw "Nao foi possivel encontrar uma letra de unidade livre para gerar o pacote."
    }

    $substDrive = "${driveLetter}:"
    & subst.exe $substDrive $root
    if ($LASTEXITCODE -ne 0) {
        throw "Nao foi possivel preparar o caminho temporario de compilacao."
    }
    $buildRoot = "$substDrive\"
}

trap {
    if ($substDrive) {
        & subst.exe $substDrive /d | Out-Null
    }
    throw $_
}

$project = Join-Path $buildRoot "Ludryn\Ludryn.csproj"
$artifactRoot = Join-Path $root "artifacts\FSE"
$packageDirectory = Join-Path $artifactRoot "Package"
$buildPackageDirectory = Join-Path $buildRoot "artifacts\FSE\Package"
$certificateDirectory = Join-Path $artifactRoot "Certificate"
$vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $vsWhere)) {
    throw "Visual Studio Installer não foi encontrado."
}

$msBuild = & $vsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1
if (-not $msBuild) {
    throw "MSBuild não foi encontrado. Instale a carga de trabalho de desenvolvimento para Windows."
}

New-Item -ItemType Directory -Force -Path $packageDirectory, $certificateDirectory | Out-Null

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq "CN=Ludryn" -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate -or $certificate.NotAfter -lt (Get-Date).AddMonths(3)) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=Ludryn" `
        -KeyUsage DigitalSignature `
        -FriendlyName "Ludryn Xbox Home App" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={text}"
        )
}

$certificatePath = Join-Path $certificateDirectory "Ludryn.cer"
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

$trustedCertificate = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
    Where-Object Thumbprint -eq $certificate.Thumbprint |
    Select-Object -First 1
if (-not $trustedCertificate) {
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
}

$trustedRootCertificate = Get-ChildItem Cert:\CurrentUser\Root |
    Where-Object Thumbprint -eq $certificate.Thumbprint |
    Select-Object -First 1
if (-not $trustedRootCertificate) {
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
}

if (-not $BuildOnly) {
    $machineRootCertificate = Get-ChildItem Cert:\LocalMachine\Root |
        Where-Object Thumbprint -eq $certificate.Thumbprint |
        Select-Object -First 1
    if (-not $machineRootCertificate) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    }

    $machineTrustedCertificate = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
        Where-Object Thumbprint -eq $certificate.Thumbprint |
        Select-Object -First 1
    if (-not $machineTrustedCertificate) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
}

Write-Host "Gerando o pacote Xbox Home App do Ludryn..."
& $msBuild $project `
    /restore `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:RuntimeIdentifier=win-x64 `
    /p:GenerateAppxPackageOnBuild=true `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageDir="$buildPackageDirectory\" `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateThumbprint=$($certificate.Thumbprint)

if ($LASTEXITCODE -ne 0) {
    throw "A geração do pacote MSIX falhou."
}

$package = Get-ChildItem $packageDirectory -Recurse -File |
    Where-Object {
        $_.Extension -in ".msix", ".appx" -and
        $_.FullName -notmatch "[\\/]Dependencies[\\/]" -and
        $_.BaseName -like "Ludryn_*"
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) {
    throw "O pacote MSIX não foi encontrado após a compilação."
}

Write-Host "Pacote criado em $($package.FullName)"
if (-not $BuildOnly) {
    Write-Host "Instalando o Ludryn como Xbox Home App..."
    $existingPackage = Get-AppxPackage -Name "Kauac.Ludryn"
    if ($existingPackage -and $existingPackage.IsDevelopmentMode) {
        Write-Host "Substituindo o registro de depuracao sem apagar os dados do Ludryn..."
        Remove-AppxPackage `
            -Package $existingPackage.PackageFullName `
            -PreserveApplicationData
    }

    Add-AppxPackage -Path $package.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    $installedPackage = Get-AppxPackage -Name "Kauac.Ludryn"
    if (-not $installedPackage) {
        throw "O Windows não confirmou o registro do pacote Ludryn."
    }
    Write-Host ""
    Write-Host "Instalação concluída."
    Write-Host "Abra Configurações > Jogos > Experiência de tela inteira e escolha Ludryn."
}

if ($substDrive) {
    & subst.exe $substDrive /d | Out-Null
}
