# Estado atual do projeto

> Documento canônico e deliberadamente curto. Ele descreve **o estado vigente**, não o histórico de implementação.
> Código, testes, Git e documentação especializada prevalecem se houver divergência. Para histórico detalhado, consulte `PROJECT_HISTORY.md` somente quando a tarefa realmente exigir contexto antigo.

## 1. Snapshot

- **Produto:** FiveMCleaner, aplicativo desktop Windows para otimização transparente, reversível e orientada por diagnóstico do FiveM para **GTAV Legacy**.
- **Integração:** `dev/proxima-versao` é a branch de integração da próxima versão; `main` representa a linha pública/estável. O fluxo de branches, worktrees, Pull Requests, integração e release é definido em `AI_RULES.md`.
- **Último estado consolidado neste documento-fonte:** 20/08/2026. Antes de qualquer trabalho, confirme o estado real com Git e os testes atuais.
- **Versão pública:** `v1.3.2`, publicada em 07/08/2026 a partir de `main`. Confirme tags/releases antes de iniciar uma nova publicação.
- **Atalho de desenvolvimento:** `FiveMCleaner - Desenvolvimento` usa `scripts\Start-DevelopmentApp.ps1`. Conforme `AI_RULES.md`, deve ser reconstruído com `scripts\Install-DevelopmentShortcut.ps1 -Build` (executado a partir do checkout/worktree da própria tarefa) ao final de toda tarefa que gerar mudanças no app — isolada ou de integração —, exceto tarefas de instalador/updater. O script espelha a árvore de trabalho atual para a pasta irmã fixa `FiveMCleaner-dev-shortcut` e aponta o atalho para essa cópia estável, então ele nunca fica órfão quando um worktree de tarefa é removido após o merge.

## 2. Objetivo e invariantes de segurança

- Priorizar mudanças pequenas, verificáveis, diagnosticáveis e reversíveis; nunca prometer ganho universal de FPS.
- Suporte operacional somente a **FiveM para GTAV Legacy**. GTAV Enhanced deve ser detectado/bloqueado com segurança até existir suporte específico.
- Nunca desativar Defender, Firewall, SmartScreen, UAC, Windows Update ou serviços essenciais; nunca criar exclusões de antivírus.
- Nunca injetar código, alterar memória de processos, instalar driver de kernel, usar hook gráfico ou baixar/executar código arbitrário como mecanismo de otimização.
- Caches e arquivos sensíveis são tratados por allowlist. Autenticação, `game-storage`, NUI storage, configurações e plugins não são lixo automático.
- Perfis **Leve, Médio e Agressivo** são composições versionadas de ações. O usuário escolhe o perfil, não uma lista arbitrária de tweaks.
- Cada ação deve ter escopo conhecido, pré-condições, validação, resultado tipado e rollback quando aplicável.
- O fluxo padrão é isolado por ação: verificar → aplicar → validar → registrar. Falha normal reverte somente a ação afetada; falha crítica pode abortar o restante. O broker elevado mantém contrato estrito e allowlisted.
- Não medir FPS ao vivo dentro do FiveM por overlay/hook. O benchmark implementado é o benchmark **standalone oficial do GTA V**, opt-in e fora de uma sessão FiveM.
- Dados indisponíveis por limitações do Windows/driver devem aparecer como indisponíveis; nunca estimar ou inventar métricas.

Documentos normativos: `docs/safety.md` e `docs/architecture.md`.

## 3. Arquitetura atual

### Solução .NET

`FiveMCleaner.slnx` separa responsabilidades. A árvore de `src/` possui nove projetos principais:

- `FiveMCleaner.App` — WPF, navegação, localização, tema, conta, apresentação, progresso e interação.
- `FiveMCleaner.Contracts` — DTOs, IDs, enums e contratos compartilhados; os estados persistidos de transação e journal são contratos duráveis append-only.
- `FiveMCleaner.Core` — catálogo de ações, perfis, planejamento e regras independentes de Windows/UI; o planejamento é puro e recebe explicitamente suas entradas variáveis.
- `FiveMCleaner.Windows` — descoberta e adaptadores Windows, filesystem, registro, diagnósticos e ações permitidas.
- `FiveMCleaner.Broker` — processo administrativo efêmero e allowlisted; sem shell/comandos arbitrários.
- `FiveMCleaner.Launcher` — inicialização/ativação do runtime e coordenação do fluxo de atualização.
- `FiveMCleaner.Updater` — atualização independente e staging/aplicação da atualização.
- `FiveMCleaner.UpdateRuntime` — contratos/estado durável usados pela cadeia de atualização e recuperação.
- `FiveMCleaner.ReleaseTool` — suporte à preparação/validação de artefatos de release.

Testes .NET ficam em `tests/FiveMCleaner.Tests/`.

A toolchain integrada usa .NET 10 LTS com SDK 10.0.303, C# 14 fixo e NuGet Central Package Management em `Directory.Packages.props`. Os testes usam xUnit v3 sobre Microsoft Testing Platform, com cobertura via `coverlet.MTP`.

### Infraestrutura e web

- `infra/cloudflare-worker/` — backend Cloudflare Worker + D1 para telemetria, relatos de bug e perfil de conta.
- `infra/dashboard/` — painel administrativo privado da telemetria/bugs.
- `website/` — fonte única do site/landing page, gerada como export estático nativo do Next.js para GitHub Pages.
- `installer/` — Inno Setup 7 em arquitetura x64.
- `scripts/` — build, validação, release, smoke tests e launcher de desenvolvimento.
- `.github/workflows/` — CI de .NET/site/Worker/dashboard, Pages, SBOM e release. Dependabot cobre NuGet, npm e Actions; o CodeQL usa o default setup do GitHub para C#, JavaScript/TypeScript e Actions.

Node 24.19 LTS é o baseline versionado para site, Worker e dashboard.

### Persistência local

Preferências, journals, solicitações efêmeras, filas e logs locais ficam sob `%LOCALAPPDATA%\FiveMCleaner`; não gravar dados mutáveis na pasta de instalação.

## 4. Estado funcional relevante

### Interface

- Aplicação WPF com WPF-UI/Fluent, Mica, tema claro/escuro/sistema e localização.
- Janela principal inicia/restaura maximizada e preserva comportamento de bandeja.
- Visão geral apresenta diagnóstico/prontidão e monitoramento local de recursos; coleta pausa quando a superfície não está ativa.
- Redesenho visual completo (20/08, direção "Câmara Âmbar"): tokens de tema (`Themes/Tokens/*.xaml`), `Controls.xaml`, `Surfaces.xaml`, `Typography.xaml` e as páginas Visão geral/Otimizador/Histórico foram redesenhadas; `ArcProgress`/`CoreVisual`/`CoreVisualPalette` (cena 3D antiga do Otimizador) foram removidos nesse redesenho.
- Aba **Otimizador**: trilha Preparar → Executar → Resultado, seleção Leve/Médio/Agressivo, resumo do computador, execução/progresso e resultado.
- Animações do Otimizador evitam `ScaleTransform` em elementos interativos, seguindo a regra já adotada para impedir deslocamento de listas no hover.
- Smoke de captura aceita seleção de página via `--capture-page=Optimizer|History|Settings|Dashboard` e tema via `--capture-theme=light|dark` (só sob `--capture=`, não persiste).
- Painel de **Notas da Versão** (`ReleaseNotesWindow`) é exibido automaticamente após um update bem-sucedido, controlado por `ReleaseNotesEvaluator`/`ReleaseNotesCatalog` e pelo campo `LastSeenReleaseNotesVersion` das configurações (mostra de novo só quando existem notas mais recentes que a última vista).
- Aviso ao vivo: ícone/banner no app consultam `GET /live-alert` (Worker) e mostram mensagem publicada pelo dashboard; dispensa é lembrada por `DismissedLiveAlertId` até o próximo aviso.
- `MainWindow.xaml.cs` e `MainViewModel.cs` são divididos em `partial class` por área de responsabilidade (ex.: `MainWindow.Navigation.xaml.cs`, `MainWindow.Capture.xaml.cs`, `MainViewModel.Progress.cs`, `MainViewModel.Settings.cs`); ao editar uma área, localize o arquivo parcial correspondente em vez de assumir um único arquivo monolítico.

### Motor de otimização e diagnóstico

- `ActionCatalog.CurrentVersion` mais recente registrado: **14**.
- Diagnósticos cobrem FiveM/GTA, CPU, GPU, RAM, armazenamento, cache, processos, rede, pagefile/commit, drivers, monitor, HAGS, energia, WHEA, sinais de throttling e outros dados obtidos por APIs nativas/best-effort.
- Existem diagnósticos somente leitura para gargalo provável, overlays/captura, logs do FiveM e orientação de medição pelas ferramentas oficiais do FiveM.
- Relatório estruturado e relatório técnico sanitizado podem ser copiados/salvos explicitamente pelo usuário.
- Relatos de bug são classificados automaticamente por `BugCodeClassifier`/`BugCode` (enum de códigos estáveis) antes do envio, para agrupar causas semelhantes no dashboard sem exigir triagem manual de texto livre.
- Journal, snapshots e rollback preservam rastreabilidade das ações; a revalidação de planos compara integralmente os metadados de ações e usa a reconstrução canônica da requisição.
- Ações XML de gráficos usam uma transação segura compartilhada; inspeção de processos e adaptadores de GPU têm primitivas de leitura separadas das mutações.
- Diagnóstico de criadores reconhece OBS, Streamlabs Desktop e TikTok LIVE Studio sem fechar processos nem inferir que uma live está ativa.

### Conta e autenticação

- Autenticação do aplicativo usa **Firebase Authentication REST** para cadastro, login, verificação de e-mail, recuperação, reautenticação, alteração de e-mail/senha e exclusão de conta.
- O ID Token fica em memória; refresh token opcional é persistido protegido por DPAPI. O **Firebase UID** é o identificador interno permanente, nunca o e-mail.
- Perfil complementar (nome, sobrenome e username único) é armazenado no Worker/D1, indexado pelo UID autenticado.
- Worker valida ID Token Firebase por RS256/JWKS, incluindo `aud`, `iss`, expiração e `sub`.
- Login com Google usa OAuth2 + PKCE com redirect loopback e foi testado ponta a ponta com credenciais reais de desenvolvimento.
- Segredos/configuração local de Google não são versionados; overlays `Config/appsettings.{Development,Production}.local.json` são git-ignorados.
- Gerenciamento de conta fica em Configurações. Avatar é normalizado e armazenado **somente localmente** por enquanto; não existe backend de avatar.

### Telemetria, bugs e backend

- FormSubmit foi removido do código de desenvolvimento. O transporte atual usa Cloudflare Worker/D1.
- Infraestrutura registrada como ativa: `/telemetry`, `POST /bugs`, `GET /api/bugs` e `GET /live-alert`/`POST /admin/live-alert` (aviso ao vivo do dashboard para o app, painel dedicado no dashboard); relatos de bug são texto, e-mail opcional e trecho de log opcional. **Não há anexo/R2**.
- Telemetria e crash reporting obedecem consentimento e allowlists; falhas de envio nunca devem bloquear ou alterar o resultado da otimização.
- Consentimento de privacidade na versão **5** (`PrivacyConsentPolicy`): diagnósticos essenciais agora incluem detecção do FiveM/GTA V, edição do GTA V e contagem de alvos; dados opcionais (sob consentimento) incluem build do Windows, tipo de disco, espaço livre, timestamp da execução, frequência de uso, backup e contagem de processos no início. Schema/migration/`INSERT` do Worker (`0004_telemetry_v5_fields.sql`) já ingerem todos os campos.
- Serviço de telemetria anônima expõe contadores de saúde (`SuccessfulSends`, `FailedSends`, `IsHealthy`) e grava falhas best-effort em `telemetry_failures.log` na pasta local da fila, sem nunca lançar para o chamador.
- Sentry é usado para crash reporting do aplicativo, com sanitização/configuração centralizada e sem transformar o SDK em dependência das camadas Core/Windows/Broker.
- Dashboard administrativo possui filtros, visão de telemetria e bugs e tratamento defensivo de falhas de rede/respostas inválidas.

### Atualização e distribuição

- Cadeia de atualização é independente/transacional, com staging, validações de origem/integridade, estado durável, health receipt, recuperação/rollback e proteção contra downgrade conforme documentação específica.
- Launcher/Updater tratam locks transitórios e corridas de processo; broker e fluxos elevados possuem timeouts para evitar bloqueio indefinido. Espera pelo processo pai é compartilhada entre Launcher e Updater via `ParentProcessWait` (UpdateRuntime); hashing/extração/verificação de pacote roda fora da UI thread com `CancellationToken` propagado; comparação de hash do manifesto é em tempo constante; `RecoveryCoordinator` completa journals órfãos quando o piso anti-downgrade já avançou por outro caminho.
- Instalador Inno Setup 7 é self-contained `win-x64`, usa setup x64 e mantém tarefas como atalho e startup configuráveis no modo interativo.
- Pipeline de endurecimento por ofuscação da release (`scripts/Invoke-Obfuscation.ps1`, config em `build/obfuscation/FiveMCleaner.Obfuscar.xml`): ofusca Core/Windows embutidos no bundle single-file do Launcher; `scripts/Test-HardenedRuntime.ps1`/`scripts/Test-NoUnobfuscatedAssemblies.ps1` validam determinismo e ausência de assemblies não ofuscados; gate fail-closed integrado a `scripts/Build-Portable.ps1`/`Build-Installer.ps1` e ao workflow de release. Ver `docs/release-hardening.md`.
- Site público, README, instalador, manifesto/checksums e release devem permanecer coerentes com a versão realmente publicada.

## 5. Pendências e decisões abertas

Somente itens ainda relevantes devem permanecer aqui. Quando resolvidos e integrados, remova-os em vez de criar uma cronologia.

1. **Ideia futura — watcher de sessão FiveM/GTA** (não é uma decisão bloqueada, é backlog de funcionalidade): ajustes que precisariam ser aplicados/restaurados durante o ciclo de vida do jogo (prioridade, afinidade, core parking, timer resolution e semelhantes) são um candidato de funcionalidade futura. Continuam fora do catálogo até existir uma arquitetura segura de monitoramento e reversão mesmo se o FiveMCleaner for encerrado. Ver `docs/graphics-optimizations-backlog.md` para o design ainda a amadurecer.
2. **GTAV Enhanced** — sem suporte operacional; requer adaptador/projeto específico antes de habilitar qualquer ação.
3. **Authenticode público** — executáveis e instalador ainda não possuem assinatura de publisher confiável; a implementação depende de certificado/conta externa e deve assinar antes dos hashes e manifestos finais.
4. **Próximas majors do frontend** — TypeScript 7 ainda excede o peer range suportado pelo `typescript-eslint` vigente, e ESLint 10 ainda não é aceito por plugins do stack Next. O estado suportado permanece TypeScript 6 e ESLint 9 até os peers oficiais convergirem.
5. **Vulnerabilidades reportadas pelo Dependabot no repositório** — o GitHub reporta 11 alertas (3 high, 8 moderate) na branch padrão; nenhuma foi triada nesta rodada de integração. Ver `https://github.com/marquezinii/FiveMCleaner/security/dependabot` antes da próxima publicação.
6. **Campos de bug-report v5 não enviados** — `reproducibility`, `severity` e `gtaEdition` foram cogitados para o relato de bug (`BugReportWindow`) junto da telemetria v5, mas ficaram fora da integração: o Worker não tem schema/validação para eles em `bug_reports`, e a UI não os preenche hoje. Requer trabalho conjunto de UI + backend antes de existir.

## 6. Baseline de validação registrada

Estes números são **referência histórica do último estado validado**, não substituem testes da branch atual.

- **20/08/2026:** integração de 6 PRs em `dev/proxima-versao` (docs: revisão de pendências; refactor: split estrutural de `MainViewModel.cs`, `MainWindow.xaml.cs` e `website/app/page.tsx` em partial classes/componentes, sem mudança de comportamento; build: pipeline de endurecimento por ofuscação da release; feat: redesenho visual completo do app na direção "Câmara Âmbar"; fix: ingestão dos campos v5 de telemetria no Worker, schema+migration+INSERT; feat: lado do app da telemetria v5, diagnósticos essenciais/opcionais expandidos). A branch experimental `ai/telemetry/v5-expanded-fields` (nunca abriu PR, base desatualizada) foi reconciliada manualmente com o PR de ingestão do Worker — mesmos nomes de campo, sem mismatch — e seus campos de bug-report (não suportados pelo Worker) ficaram de fora, ver pendência 6. O conflito entre o redesenho visual e o split de `MainWindow.xaml.cs` (mesma região de código movida por ambas as branches) foi resolvido semanticamente realocando o método `CaptureIfRequestedAsync`/`--capture-theme=` para `MainWindow.Capture.xaml.cs`. Após integração: restore e build .NET Release sem warnings, **970 testes .NET**, `dotnet format --verify-no-changes`, `scripts/Verify-Safety.ps1` e `git diff --check` aprovados; Worker com **196 testes** e `npm audit` sem vulnerabilidades; site com `typecheck`, `lint`, build estático e **3 testes** aprovados; dashboard não tocado nesta rodada (último baseline: 47 testes). Atalho `FiveMCleaner - Desenvolvimento` reconstruído.

Ao alterar uma superfície, execute a validação aplicável novamente e use os resultados atuais no PR. Nunca use estes números para afirmar que código posterior foi testado.

## 7. Comandos essenciais

Na raiz:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
dotnet format FiveMCleaner.slnx --verify-no-changes
.\scripts\Verify-Safety.ps1
git diff --check
.\scripts\Start-DevelopmentApp.ps1
```

Worker:

```powershell
Set-Location infra\cloudflare-worker
npm test
npm audit
```

Dashboard/site: execute testes, lint, typecheck e build definidos nos respectivos `package.json` quando a superfície for alterada.

Build/distribuição, quando aplicável:

```powershell
.\scripts\Build-Portable.ps1
.\scripts\Build-Installer.ps1 -Version <versão>
```

## 8. Release e operações remotas

- `main` não recebe desenvolvimento normal. Integração ocorre em `dev/proxima-versao`; publicação oficial segue `AI_RULES.md`.
- Não inferir autorização de push/deploy/release a partir de um commit local ou de uma validação bem-sucedida.
- Antes de calcular versão ou publicar, confirme tags/releases reais e o diff desde `v1.3.2`, a versão pública confirmada neste snapshot.
- Deploy do Worker, Pages, release, tags, assets e demais operações remotas devem seguir as permissões e gatilhos definidos em `AI_RULES.md`.
- Release pública exige coerência entre código, versão, `CHANGELOG.md`, GitHub Release, instalador, updater, site e artefatos.

## 9. Documentação a consultar por domínio

Leia somente quando a tarefa tocar o domínio correspondente:

- `AI_RULES.md` — governança obrigatória de agentes, Git, PRs, integração e release.
- `docs/safety.md` — limites de segurança e operações proibidas.
- `docs/architecture.md` — fronteiras e contratos arquiteturais.
- `docs/telemetry.md` — contrato de telemetria/privacidade.
- `docs/installer.md` — instalador e release.
- `docs/graphics-optimizations-backlog.md` — decisões e backlog técnico de otimizações.
- `infra/cloudflare-worker/README.md` — operação/configuração do Worker e conta.
- `PROJECT_HISTORY.md` — histórico detalhado; **não é leitura padrão**.

## 10. Regra de manutenção deste arquivo

`PROJECT_STATE.md` deve continuar pequeno. Ele não é changelog, diário de agente, relatório de PR nem arquivo de auditoria.

- Atualizar preferencialmente **após integração**, refletindo somente o estado consolidado.
- Não registrar nomes de agentes, branches temporárias, hashes de commits ou uma seção por tarefa concluída.
- Não duplicar documentação especializada; apontar para o documento canônico.
- Manter apenas: arquitetura vigente, invariantes, capacidades atuais, decisões abertas, pendências reais e último baseline de validação.
- Ao resolver uma pendência, removê-la ou substituir o estado correspondente; não preservar a história da resolução aqui.
- Se uma informação for apenas histórica, movê-la para `PROJECT_HISTORY.md` ou deixá-la no Git/PR correspondente.
- **Meta operacional:** aproximadamente 200 linhas e preferencialmente menos de 20 KB. Se ultrapassar isso de forma sustentada, compactar novamente antes de adicionar novas seções.
