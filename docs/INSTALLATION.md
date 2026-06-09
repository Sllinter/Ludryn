# Instalação do Ludryn

## Pré-requisitos

- Windows 11 atualizado.
- Computador compatível com o Xbox Full Screen Experience.
- Uma conta de administrador para concluir a instalação.

## 1. Habilite o Xbox Full Screen Experience

Baixe a versão mais recente do
[Xbox Full Screen Experience Tool](https://github.com/ashpynov/XboxFullScreenExperienceTool/releases/latest),
execute a ferramenta e reinicie o Windows caso seja solicitado.

## 2. Instale o Ludryn

1. Baixe [Ludryn-Setup.exe](../../releases/latest/download/Ludryn-Setup.exe).
2. Execute o arquivo.
3. Confirme a solicitação de administrador.
4. Quando perguntado, confirme que o FSE já foi habilitado.
5. Aguarde a mensagem **Ludryn instalado**.

O Setup instala automaticamente:

- O pacote MSIX do Ludryn.
- O Windows App Runtime necessário.
- O certificado usado para validar o pacote.
- A integração que permite selecionar o Ludryn como Xbox Home App.

## 3. Selecione o Ludryn como Home App

Abra no Windows:

```text
Configurações > Jogos > Experiência de tela inteira
```

Selecione **Ludryn** como aplicativo inicial.

## 4. Configure as artes

No Ludryn, abra `Configurações > SteamGridDB`, crie uma chave no site oficial
e cole-a no aplicativo. Sem uma chave, os jogos continuam funcionando, mas
podem aparecer sem as artes online.

## Atualizações

Execute o Setup de uma versão mais recente. O instalador substitui a versão
anterior e preserva as configurações, bibliotecas, perfis e cache do usuário.

## Solução de problemas

Ao relatar um problema, informe:

- A versão do Windows.
- A versão do Ludryn.
- O que estava sendo feito antes do erro.
- Os arquivos da pasta `Logs` do Ludryn, quando existirem.

Use a [página de issues](../../issues/new/choose) para enviar o relato.
