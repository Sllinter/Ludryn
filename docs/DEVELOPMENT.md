# Desenvolvimento

## Requisitos

- Windows 11.
- Visual Studio com desenvolvimento para Windows e MSBuild.
- .NET 8 SDK.
- Windows App SDK.

## Compilação

```powershell
dotnet build Ludryn.slnx -p:Platform=x64
```

## Gerar um instalador

Atualize a versão em `Ludryn/Package.appxmanifest` e execute:

```powershell
.\Build-LudrynRelease.ps1
```

Os arquivos finais serão criados em:

```text
artifacts\Release\
```

## Publicar uma versão

1. Atualize `CHANGELOG.md`.
2. Atualize a versão no manifesto.
3. Faça commit das alterações.
4. Crie uma tag com a mesma versão, por exemplo `v1.0.3.0`.
5. Envie a tag ao GitHub.

O workflow valida a versão, compila o instalador e cria a GitHub Release.

## Dados locais

Chaves, caches, logs, certificados e pacotes gerados não devem ser enviados
ao Git. Consulte `.gitignore` antes de publicar alterações.
