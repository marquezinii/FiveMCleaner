# Instalador, atualização e publicação

O instalador oficial do FiveMCleaner é um executável Inno Setup moderno para
Windows 10 build 19041 ou mais recente e Windows 11, em sistemas compatíveis com
binários x64. Ele instala por usuário em `{autopf}\FiveMCleaner`; por padrão,
isso corresponde à pasta de programas local do usuário e não exige UAC.

## Dependências e funcionamento offline

O aplicativo WPF e o broker administrativo são publicados como `win-x64`
**self-contained**, em múltiplos arquivos e sem trimming. O runtime do .NET
Desktop, o CoreCLR e as bibliotecas nativas ficam dentro do instalador. O PC do
usuário não precisa ter o .NET instalado e a instalação não baixa scripts,
runtimes ou pacotes da internet.

Essa escolha é intencional: elimina falhas de proxy/rede no primeiro uso e evita
executar conteúdo remoto que possa mudar depois de a release ser criada. O
broker continua separado e pede elevação somente quando uma ação protegida do
Windows realmente for executada.

## Experiência do instalador

- português do Brasil e inglês, escolhidos pela interface do Windows;
- tema moderno que acompanha o modo claro/escuro do sistema;
- ícone e imagem oficiais do FiveMCleaner;
- atalhos do menu Iniciar e desinstalação completa;
- atalhos de Área de Trabalho e inicialização com o Windows opcionais e
  desmarcados por padrão;
- upgrade no mesmo diretório por meio de um `AppId` estável;
- Windows Restart Manager para solicitar o fechamento seguro do app durante um
  upgrade, sem encerramento forçado nem reinicialização automática;
- logs padrões do Inno Setup para diagnóstico.

Configurações, journals, logs, backups e downloads de atualização ficam fora da
pasta de instalação, em `%LOCALAPPDATA%\FiveMCleaner`. Na desinstalação
interativa, a pessoa escolhe se deseja preservar ou remover esses dados. A
opção padrão é preservar; uma desinstalação silenciosa também preserva os dados
para nunca apagar histórico ou backup sem confirmação visível.

## Build local reproduzível

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.0

$installer = Resolve-Path .\artifacts\installer\FiveMCleaner-Setup-1.0.0-win-x64.exe
.\scripts\Test-Installer.ps1 `
  -InstallerPath $installer `
  -PublishDirectory .\artifacts\FiveMCleaner-win-x64 `
  -ExpectedVersion 1.0.0
```

O script primeiro executa a verificação de segurança e o publish self-contained,
depois compila o instalador, gera SHA-256 e um manifesto de release. Se o Inno
Setup 6.7.3 não estiver instalado, o build baixa a release imutável oficial para
um cache dentro de `artifacts/.tools`, exige o SHA-256 fixado no script e valida
a assinatura Authenticode de `Pyrsys B.V.` antes de executar o compilador.

O teste instala silenciosamente em uma pasta temporária sob `artifacts`, confere
byte a byte todo o payload, valida a entrada opcional de inicialização, executa a
desinstalação e confirma a remoção. Ele se recusa a rodar se encontrar uma
instalação real ou uma entrada de inicialização existente.

## Contrato de atualização

O arquivo `installer/release-contract.json` define nomes estáveis para o
instalador, checksum, manifesto e pacote portátil. O aplicativo deve consultar
somente a API oficial da última release estável do GitHub, comparar versões e
mostrar uma notificação; ele não deve forçar, baixar ou instalar uma atualização
sem confirmação.

Depois do clique do usuário, o atualizador:

1. exibe a página oficial das alterações da release, quando disponível;
2. baixa somente o instalador com nome permitido, via HTTPS e de origem GitHub
   validada;
3. confere tamanho, digest SHA-256 fornecido pela API do GitHub e hash do
   arquivo baixado antes de qualquer abertura;
4. grava o pacote concluído de forma atômica em
   `%LOCALAPPDATA%\FiveMCleaner\Updates` e mantém arquivos parciais sem uso;
5. pede uma segunda confirmação e abre o instalador interativo normal; o
   Restart Manager solicita o fechamento seguro do app para a substituição;
6. nunca desativa SmartScreen, Defender, UAC ou antivírus de terceiros.

Previews não aparecem no endpoint `/releases/latest`; portanto, uma instalação
estável nunca migra para uma preview sem uma escolha explícita de canal.

Se a consulta, download ou validação falhar, nenhum instalador é aberto e a
versão já instalada continua utilizável. O setup aceita atualização por cima
da instalação anterior e preserva os dados locais. O Inno Setup não fornece um
rollback transacional completo após uma falha externa durante a cópia de
arquivos; nesse caso a pessoa deve executar novamente o instalador da mesma
release ou a release anterior verificada. Isso é uma limitação explicitamente
assumida, não uma promessa de rollback automático.

## Publicação no GitHub

O workflow `.github/workflows/release.yml` só aceita disparo manual. Com
`publish=false`, ele compila, testa e guarda os artefatos apenas dentro da
execução. A criação pública exige uma tag exata (`vX.Y.Z` ou
`vX.Y.Z-preview`), `publish=true` e o canal correspondente.

Antes de criar a release, o workflow repete build, testes, instalação e
desinstalação; gera checksums; e produz uma atestação de proveniência do
instalador. O binário permanece sem assinatura de código até existir um
certificado Authenticode. SHA-256 e atestação aumentam a transparência, mas não
substituem reputação ou uma assinatura pública.

Fontes oficiais usadas no desenho:

- [Inno Setup: recursos e suporte de Windows](https://jrsoftware.org/isinfo.php)
- [Inno Setup: modo não administrativo](https://jrsoftware.org/ishelp/topic_admininstallmode.htm)
- [Inno Setup: AppId e upgrades](https://jrsoftware.org/ishelp/topic_setup_appid.htm)
- [Inno Setup: tema moderno e dinâmico](https://jrsoftware.org/ishelp/topic_setup_wizardstyle.htm)
- [Inno Setup: Restart Manager](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm)
- [Inno Setup: verificação dos downloads oficiais](https://jrsoftware.org/isdl-verify.php)
- [GitHub: releases em workflows](https://docs.github.com/actions/using-workflows/events-that-trigger-workflows#workflow_dispatch)

## Procedimento de release

1. Atualize `Directory.Build.props` e `CHANGELOG.md` com uma versão SemVer.
2. Execute localmente `Verify-Safety.ps1`, os testes, `Build-Installer.ps1` e
   `Test-Installer.ps1`.
3. Faça commit, envie `main`, crie a tag exata `vX.Y.Z` e envie a tag.
4. Em **Actions → Build installer and publish release**, escolha a tag, canal
   `stable` e `publish=true`.
5. Verifique no GitHub a release pública, o instalador, os checksums, o
   manifesto e a atestação antes de divulgar o link.

O workflow nunca publica por `push`; a etapa de criação de release exige o
disparo manual com `publish=true`. A página pública de download é
`https://github.com/marquezinii/FiveMCleaner/releases/latest`, gratuita e sem
login para visitantes.
