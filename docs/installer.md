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
- tema moderno que acompanha o modo claro/escuro do sistema, com arte lateral
  clara e escura gerada a partir do ícone oficial;
- ícone e imagem oficiais do FiveMCleaner;
- atalhos do menu Iniciar e desinstalação completa, com rótulos localizados;
- atalho de Área de Trabalho habilitado por padrão; inicialização com o Windows
  desmarcada por padrão (ambas alteráveis na instalação e depois);
- página final lembra que atualizações futuras vêm pelo app, com confirmação;
- compressão `lzma2/ultra` no pacote offline self-contained;
- upgrade no mesmo diretório por meio de um `AppId` estável;
- Windows Restart Manager para solicitar o fechamento seguro do app durante um
  upgrade, sem encerramento forçado nem reinicialização automática;
- logs padrões do Inno Setup para diagnóstico (pasta temporária quando ativos).

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
byte a byte todo o payload, valida o padrão desktop-on/startup-off, a task de
inicialização quando pedida, o handoff `/AUTOUPDATE=yes`, a preservação de
dados em `%LOCALAPPDATA%\FiveMCleaner` no uninstall silencioso, executa a
desinstalação e confirma a remoção. Ele se recusa a rodar se encontrar uma
instalação real ou uma entrada de inicialização existente. Somente para uma
validação local explicitamente autorizada, `-AllowExistingInstallation` libera
essa trava; como o AppId é o mesmo, esse modo pode alterar o registro da
instalação local e não deve ser usado como gate de release.

Em CI (`GITHUB_ACTIONS`/`CI`), `Build-Installer.ps1` recusa worktree suja para
não publicar manifesto com `sourceDirty=true`. Localmente o build continua
permitido; use `-AllowDirtySource` só se precisar forçar o mesmo em CI.

## Contrato de atualização

O Inno Setup é somente o instalador inicial e a ponte para instalações legadas.
Atalhos apontam para `FiveMCleaner.Launcher.exe`; cada versão do app fica
imutável em `Runtime\versions\<versão>`. O aplicativo consulta somente o
manifesto estável assinado do Worker e nunca atualiza sem confirmação.

Depois do clique do usuário, o atualizador:

1. exibe a página oficial das alterações da release, quando disponível;
2. valida contrato fechado, assinatura ECDSA P-256, chave pública incorporada,
   SemVer, `minimumAllowedVersion`, URL GitHub allowlisted, tamanho e SHA-256;
3. baixa somente `FiveMCleaner-Runtime-win-x64.zip` via TLS 1.2/1.3, valida
   revogação e cada redirecionamento, limita tamanho e grava com nome parcial;
4. valida novamente o ZIP e o `SHA256SUMS.txt`; arquivos extras, ausentes,
   duplicados, alterados, caminhos externos e pacotes de extração excessiva são
   rejeitados;
5. move a árvore completa para `Runtime\versions\<versão>`, registra journal e
   troca `active.json` atomicamente;
6. fecha o app anterior e chama o launcher. A candidata precisa gravar um
   health receipt com nonce em até 45 segundos;
7. sem receipt, o launcher restaura somente o predecessor registrado. Uma
   versão saudável avança o piso anti-downgrade protegido por DPAPI;
8. nunca desativa SmartScreen, Defender, UAC ou antivírus de terceiros.

Falhas de manifesto, download, staging, ativação e saúde preservam a versão
anterior. Logs detalhados ficam locais; eventos sanitizados chegam à área
administrativa somente após consentimento explícito de telemetria.

## Publicação no GitHub

O workflow `.github/workflows/release.yml` só aceita disparo manual. Com
`publish=false`, ele compila, testa e guarda os artefatos apenas dentro da
execução. A criação pública exige uma tag exata (`vX.Y.Z` ou
`vX.Y.Z-preview`), `publish=true` e o canal correspondente.

Antes de criar a release, o workflow repete build, testes, instalação e
desinstalação; gera checksums; assina e verifica o manifesto do runtime; aplica
o schema D1; implanta o Worker/feed; e produz uma atestação de proveniência do
instalador. O binário permanece sem assinatura de código até existir um
certificado Authenticode. SHA-256 e atestação aumentam a transparência, mas não
substituem reputação ou uma assinatura pública.

### Sequência de versões públicas

A versão segue SemVer conforme `AI_RULES.md`: correção compatível usa patch,
nova capacidade compatível usa minor e mudança incompatível usa major. O script
`scripts/Test-PublicVersionProgression.ps1` recusa regressões e saltos que não
sejam uma progressão SemVer pública válida.

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
5. Verifique no GitHub a release pública, o instalador, o runtime ZIP, os
   checksums, o manifesto assinado e a atestação; confirme também o feed do
   Worker antes de divulgar o link.

O workflow nunca publica por `push`; a etapa de criação de release exige o
disparo manual com `publish=true`. A página pública de download é
`https://marquezinii.github.io/FiveMCleaner/`, gratuita e sem login para
visitantes. O botao da pagina inicia diretamente o alias estavel
`FiveMCleaner-Setup-latest-win-x64.exe` hospedado na release oficial.
