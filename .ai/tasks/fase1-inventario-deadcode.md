# Fase 1 — Inventário arquitetural e censo de dead code

Data: 2026-08-13. Branch: `task/fase1-inventario-deadcode` (base `origin/dev/proxima-versao`). Investigação somente leitura, salvo indicação contrária; nenhuma remoção de código foi feita nesta fase (ver justificativa na seção final).

## Resumo executivo

- **9 projetos .NET + testes**: grafo de dependências correto, sem violação de fronteira, sem pacote/ProjectReference órfão.
- **Windows Infrastructure/Engine (Fase 2/3)**: nenhum dead code. 2 duplicações reais (leitura de GPU via Registro; matcher "processo pertence à instalação") e 1 achado arquitetural (Inspector que muta estado) para fases futuras.
- **Action Catalog (Fase 4)**: matriz 61/61 IDs íntegra, sem órfãos. 1 DEAD-PROVEN real (`ActionReversibility.SessionScoped`) — não removido por risco de compatibilidade com journals persistidos de usuários reais (produto já publicado). 2 duplicações para consolidar.
- **App WPF (Fase 5)**: o maior cluster de DEAD-PROVEN real (ícones/estilos XAML órfãos do redesign do Otimizador, ~9 propriedades de ViewModel órfãs, algumas chaves `.resx`, 1 asset fora do build). Não removido nesta fase por não ser "1 linha" (múltiplos call-sites) — fica para execução coesa na Fase 5.
- **Broker/IPC (Fase 6)**: fronteira de privilégio íntegra — todo comando tem produtor e executor. 1 campo DEAD-PROVEN de baixo risco (`BrokerEventWire.ErrorCode`) e 1 duplicação de contrato IPC — ambos deixados para a Fase 6 por tocarem o canal do processo elevado.
- **Launcher/Updater/ReleaseTool/installer/scripts/CI (Fase 7)**: cadeia 100% viva, sem script/step órfão.
- **Worker/dashboard/website (Fase 8)**: nenhum dead code — rodada de tech-debt anterior já limpou. 1 gap operacional (secret não documentado) e 1 migration D1 legada (não apagar).
- **Transversal config/testes/docs (Fase 9)**: nenhum dead code. 2 trechos de documentação desatualizada em `docs/graphics-optimizations-backlog.md`.
- **Nenhuma remoção de código foi feita nesta fase.** Ver "Por que nada foi removido" no final.

## ⚠️ Divergência relevante: Fase 0 não integrada

A branch `refactor/phase0-contracts-core` (worktree `.claude/worktrees/phase0-foundation`) contém 3 commits e 27 arquivos alterados (`Contracts/ActionMetadataDto.cs`, novo `Contracts/TransactionEnums.cs`, `Contracts/OptimizationEnums.cs`, `Core/Planning/PlanBuilder.cs` + novo `PlanBuildContext.cs`, `Windows/Engine/WindowsTransactionEngine.cs` e afins, `Broker/PlanValidator.cs`, `Broker/Program.cs`, `App/Services/AppOptimizationService.cs`, `App/ViewModels/MainViewModel.cs`) que **ainda não foram mergeados em `dev/proxima-versao`**. Este censo foi feito sobre o estado atual de `dev/proxima-versao` (`beb5fe1`), não sobre o resultado da Fase 0. Como essas áreas se sobrepõem diretamente aos achados deste relatório (especialmente Contracts/OptimizationEnums.cs e o motor transacional), **as Fases 2–4 devem reverificar os achados aqui relevantes após a integração da Fase 0**, conforme o próprio gate definido em CLEANING.md §Fase 0.

## Grafo de dependências .NET (resolvido diretamente, sem agente)

- `App` → `Contracts`, `Core`, `Windows`, `UpdateRuntime` (+ `Broker` só via `MSBuild Target CopyBroker`/`ReferenceOutputAssembly=false` — cópia de binário, sem link de assembly: confirma isolamento de processo do Broker)
- `Broker` → `Contracts`, `Core`, `Windows`
- `Contracts` → (nenhuma dependência de projeto)
- `Core` → `Contracts`
- `Windows` → `Contracts`, `Core`
- `Launcher` → `UpdateRuntime`
- `ReleaseTool` → `UpdateRuntime`
- `Updater` → **nenhuma ProjectReference** (por design: `Program.cs`+`UpdateHandoff.cs` só fazem handoff/verificação de hash SHA-256/execução do instalador; precisa sobreviver isolado enquanto o processo pai é substituído, então não pode depender de assemblies que podem estar sendo trocados)
- `UpdateRuntime` → (nenhuma dependência de projeto; só `System.Security.Cryptography.ProtectedData`)
- `Tests` → `UpdateRuntime`, `App`, `Contracts`, `Core`, `Windows`, `Updater` (Broker não é referenciado por nenhum teste — lacuna de cobertura, ver seção Broker/IPC)

Nenhuma dependência circular. Nenhuma violação de fronteira encontrada (App/Core sem privilégio elevado direto; Contracts sem conhecimento de implementação/UI; Broker mínimo).

**Pacotes NuGet centralizados (`Directory.Packages.props`):** todos os 8 (`coverlet.MTP`, `Microsoft.Testing.Platform`, `Sentry`, `System.Diagnostics.PerformanceCounter`, `System.Management`, `System.Security.Cryptography.ProtectedData`, `WPF-UI`, `xunit.v3.mtp-v2`) têm `PackageReference` correspondente em pelo menos um `.csproj`. Nenhum pacote órfão no CPM.

**UpdateRuntime (11 tipos):** `AtomicFile`, `RecoveryCoordinator`, `RuntimeActivationStore`, `RuntimePackageStager`, `SignedBrokerManifest`, `SignedReleaseManifest`, `TransientRetry`, `UpdateHealthReceiptStore`, `UpdateRecoveryJournal`, `UpdaterDiagnostics`, `VersionFloorStore` — todos com 2 a 7 consumidores cruzados confirmados. Nenhum órfão.

## Windows Infrastructure / Engine (Fases 2 e 3)

**Método:** composition root manual em `WindowsOptimizationRuntime.cs:120-144` (sem container DI — tudo `new` direto), acionado por `AppOptimizationService.cs:1039`. A varredura ingênua por nome de classe (`\bNome\b`) deu falso positivo em todos os 21 Inspectors porque o regex não bate dentro de `WindowsNome`/`INome` (sem fronteira de palavra).

**Resultado:** nenhum DEAD-PROVEN. Todos os Inspectors/Engine/Journal são LIVE via composition root.

### Achados de duplicação (não são dead code — candidatos a consolidação na Fase 2)

1. **DUPLICATE** — leitura de adaptador de vídeo via Registro quase idêntica em `WindowsGpuVendorInspector.GetSnapshot()` (`src/FiveMCleaner.Windows/Infrastructure/GpuVendorInspector.cs:21-62`) e `WindowsGpuDetailsInspector.GetSnapshotInternal()` (`src/FiveMCleaner.Windows/Infrastructure/GpuDetailsInspector.cs:74-124`). Candidato a helper compartilhado (`WindowsGpuAdapterRegistryReader`).
2. **DUPLICATE** — padrão "processo pertence a esta instalação" repetido em 3 lugares: `FiveMProcessInspector.cs:12-64`, `GtaVProcessInspector.cs:12-64`, `StuckFiveMProcessInspector.cs:67-95`. Candidato a matcher comum.

### Achado arquitetural (SUSPICIOUS, não é dead code)

3. `StuckFiveMProcessInspector.TryTerminate` (`src/FiveMCleaner.Windows/Infrastructure/StuckFiveMProcessInspector.cs:49-65`) muta estado (`process.Kill`) apesar do nome/convenção "Inspector = somente leitura" seguida pelos outros 20 arquivos do diretório. Em uso ativo (`FiveMInstallationActions.cs:487`), não é dead code — é questão de nomenclatura/local para a Fase 3 avaliar.

| Símbolo/Arquivo | Classificação | Consumidores | Evidência | Risco | Fase |
|---|---|---|---|---|---|
| Todos os 21 Inspectors + CommandRunner/RegistryStore/TransactionJournal/WindowsTransactionEngine/OptimizationReportBuilder | LIVE | `WindowsOptimizationDependencies` via composition root | `WindowsOptimizationRuntime.cs:120-144` | — | — |
| GpuVendorInspector / GpuDetailsInspector (leitura Registro) | DUPLICATE | ambos LIVE | ver acima | Médio | Fase 2 |
| FiveMProcessInspector / GtaVProcessInspector / StuckFiveMProcessInspector (matcher) | DUPLICATE | todos LIVE | ver acima | Médio | Fase 2 |
| StuckFiveMProcessInspector.TryTerminate | SUSPICIOUS (arquitetural) | `FiveMInstallationActions.cs:487` | mutação em tipo "Inspector" | Baixo-Médio | Fase 3 |

## Action Catalog / Planning / Windows Actions (Fase 4)

**Matriz catálogo↔implementação: 61/61 IDs em `OptimizationActionIds.cs` mapeados 1:1 no switch `WindowsOptimizationActionFactory.CreateAction` (`src/FiveMCleaner.Windows/WindowsOptimizationRuntime.cs:429-668`). Nenhum ID órfão em nenhuma direção.** Validado também por `WindowsOptimizationRuntimeTests.cs:18-19` (round-trip automático), `ActionCatalogTests.cs:9-44` (IDs únicos, sem propriedades de execução perigosas) e cobertura completa nos 3 `.resx` (61 chaves `Actions.<id>.Name` em cada idioma).

`LegacyGraphicsAction.cs`/`LegacyGraphicsPresetAction` é **LEGACY-LIVE** por nomenclatura apenas — "Legacy" é terminologia de domínio (FiveM Legacy vs Enhanced), não código morto; implementa 8 dos 61 IDs ativamente.

### DEAD-PROVEN confirmado (candidato a remoção trivial nesta fase)

- **`ActionReversibility.SessionScoped`** (`src/FiveMCleaner.Contracts/OptimizationEnums.cs:39`) — busca no repositório inteiro retorna só a própria declaração. Nenhum `Define(...)` do catálogo usa (todos usam `ReadOnly`/`FullyReversible`/`RebuildableData`/`Irreversible`), nenhum switch/teste/XAML/resx referencia. Sem uso em serialização/journal persistido. Risco de remoção: muito baixo.

### Duplicações (Fase 4)

1. **DUPLICATE** — `LegacyGraphicsPresetAction` (`LegacyGraphicsAction.cs:166-698`) e `DisplayPreferencesAction` (`DisplayPreferencesAction.cs`) reimplementam de forma independente (cópia, não herança) o mesmo padrão backup+hash+replace atômico+rollback para XML. O próprio doc-comment de `DisplayPreferencesAction.cs:16-18` admite a duplicação. Em contraste, `GtaVLaunchParametersActionBase` já resolveu o mesmo problema com base abstrata compartilhada — modelo a seguir.
2. **DUPLICATE (menor)** — guarda "jogo precisa estar fechado" repetida inline em `CleanupActions.cs` (4x: linhas 50,93,201,251), `FiveMInstallationActions.cs` (533,600), `DisplayPreferencesAction.cs` (361-362), `LegacyGraphicsAction.cs` (584-585).

| Símbolo/Arquivo/ID | Classificação | Consumidores | Evidência | Risco | Fase |
|---|---|---|---|---|---|
| Catálogo de 61 Actions + implementações Windows/Actions | LIVE | Factory switch, PlanBuilder, testes, resx×3 | `WindowsOptimizationRuntime.cs:429-668` | — | — |
| `LegacyGraphicsAction.cs` | LEGACY-LIVE (nomenclatura) | 8 IDs de preset gráfico | `LegacyGraphicsAction.cs:166-698` | Nenhum | — |
| `ActionReversibility.SessionScoped` | **DEAD-PROVEN** | Nenhum | `OptimizationEnums.cs:39` | Muito baixo | Fase 4 (remoção trivial permitida na Fase 1) |
| LegacyGraphicsPresetAction vs DisplayPreferencesAction (safe-XML-write) | DUPLICATE | Ambos LIVE | ver acima | Baixo | Fase 4 |
| Guarda "jogo fechado" repetida | DUPLICATE (menor) | 4 arquivos | ver acima | Muito baixo | Fase 4 (opcional) |
| `ActionMetadataDto` (todos campos) | LIVE | Engine, ReportBuilder, PlanValidator, MainViewModel, resx | `ActionMetadataDto.cs:1-69` | — | — |
| Demais enums de `OptimizationEnums.cs` | LIVE | Catálogo, PlanBuilder, MainViewModel | — | — | — |

## Worker / Dashboard / Website (Fase 8)

**Nenhum DEAD-PROVEN encontrado.** A rodada de tech-debt anterior já removeu os órfãos óbvios; estado vigente coeso. `PROJECT_STATE.md` lista "infra ativa" de forma incompleta (não é lista de exclusão) — rotas adicionais confirmadas LIVE: `POST /updater-events`, `GET /update/manifest`, `/account/profile` GET/POST, `/admin/login`/`logout`, `/api/stats/*`, `/api/updater-events`, todas com consumidor .NET/dashboard confirmado.

| Item | Classificação | Consumidores | Evidência | Risco | Fase |
|---|---|---|---|---|---|
| Todas as rotas de `infra/cloudflare-worker/src/index.js` | LIVE | App .NET + dashboard | `src/index.js:94-144` | — | — |
| `STATS_BUILDERS` (11 entradas) | LIVE | `dashboard/assets/app.js:33-48,220-230` | `src/index.js:50-62` | — | — |
| `env.RELEASE_MANIFEST_JSON` | SUSPICIOUS (gap operacional) | Usado em produção e testado, mas não documentado em `wrangler.toml`/README como os demais secrets — pode indicar rota `/update/manifest` retornando 503 em produção | `src/index.js:147` | Médio (operacional, não código morto) | Fase 8 — auditar deploy, não remover código |
| Migration `0001_account_username_terms.sql` (`user_accounts`, `user_registration_attempts`) | LEGACY-LIVE | Sem caller no código atual; reconhecida como legado no próprio `README.md:173-175` | `migrations/0001_...sql` | Alto se apagar a migration | Fase 8 — decisão de limpeza remota autorizada separadamente |
| Comentário sobre `attachment_key`/R2 em `schema.sql:73-79` | SUSPICIOUS (doc obsoleta) | Coluna não existe mais na tabela real (R2 foi abandonado) | `schema.sql:73-79` | Nenhum | Fase 9 — limpar comentário |
| Dashboard (`api.js`, `charts.js`, `rendering.js`, `styles.css`, assets) | LIVE (100%) | Todos importados/usados em `app.js`/`index.html` | — | — | — |
| Website (`page.tsx`, `globals.css`, `next.config.ts`, assets, deps npm) | LIVE (100%) | Export estático único, validado por `tests/rendered-html.test.mjs` | — | — | — |
| Inconsistência pt/en na FAQ sobre telemetria (achado à parte, não dead code) | N/A | `page.tsx:233` (pt) vs `page.tsx:478` (en) — mensagens de privacidade contraditórias | — | Médio | Já sinalizado como task separada pelo agente (fora do escopo desta fase) |

## Broker / IPC / Distribuição (Fases 6 e 7)

**Fluxo confirmado:** `AppOptimizationService` → `ElevatedBrokerClient.ExecuteAsync/RollbackAsync` → grava `OptimizationPlanDto` em `%LOCALAPPDATA%\FiveMCleaner\Requests\{planId}.json` → `FiveMCleaner.Broker.exe --request/--rollback --pipe {guid}` elevado → `BrokerCommandLine.Parse` → `PlanRequestFileLoader`+`PlanValidator` (revalida contra `ActionCatalog.Current`) → `WindowsAdministratorRuntimeAdapter` → `WindowsTransactionEngine` → eventos via `NamedPipeEventWriter`. As duas operações do Broker (ExecutePlan, Rollback) e as duas ações `RequiredPrivilege.Administrator` do catálogo têm produtor e executor confirmados. Installer, os 11 scripts em `scripts/` e os 4 workflows formam cadeia de chamadas fechada — nenhum script/step órfão.

| Item | Classificação | Consumidores | Evidência | Risco | Fase |
|---|---|---|---|---|---|
| `BrokerCommandLine`/operações ExecutePlan/Rollback | LIVE | `ElevatedBrokerClient.cs:62-91` | — | — | — |
| `WindowsOptimizationRuntime.ResolveAdministratorActions(IEnumerable<(string,int)>)` | SUSPICIOUS | Único caller é teste (`WindowsOptimizationRuntimeTests.cs:84-89`); produção usa a outra sobrecarga | `WindowsOptimizationRuntime.cs:842-866` vs `WindowsAdministratorRuntimeAdapter.cs:30` | Método enforça invariante de privilégio administrator — não classificado DEAD-PROVEN por sensibilidade | Fase 6 (revisão humana, não remoção mecânica) |
| `BrokerEventWire.ErrorCode` | DEAD-PROVEN | Nenhum — desserializado mas nunca lido | `ElevatedBrokerClient.cs:464`; `ElevatedBrokerResult` nem tem campo correspondente | Baixo, mas é campo do protocolo IPC do processo elevado (Broker) | Fase 6 — decisão do responsável pela fronteira de privilégio, não remoção nesta fase |
| `BrokerEventKind/BrokerEvent` (Broker) vs `BrokerEventKindWire/BrokerEventWire` (App) | DUPLICATE | Ambos LIVE — protocolo IPC definido independentemente em 2 projetos sem referência de assembly entre si | `BrokerEvents.cs:3-41` vs `ElevatedBrokerClient.cs:429-467`; risco de drift de `SchemaVersion` | Consolidável em `Contracts` (ambos já referenciam) | Fase 6 (canal IPC elevado — tratar como sensível) |
| Cobertura de teste do Broker | SUSPICIOUS (lacuna, não dead code) | `tests/FiveMCleaner.Tests.csproj` não referencia `FiveMCleaner.Broker`; cobertura só indireta via testes do App | — | Lacuna de verificação em componente elevado | Reportar para Fase 6, não é item de limpeza |
| Installer, scripts/ (11), workflows/ (4), ReleaseTool (generate-key/sign/verify/sign-broker/verify-broker) | LIVE (100%) | Cadeia de chamadas fechada, confirmada cruzando scripts↔workflows↔docs | — | — | — |

## Transversal: config, testes, docs (Fase 9)

**`.ai/tasks/` (34 relatórios anteriores):** nenhum censo de dead-code prévio equivalente. `opencode-overengineering-reduction.md` (integrado 02/08/2026) já removeu itens órfãos de uma rodada anterior (streaming UI não usada, chaves RESX órfãs, `GtaVCommandLineFile.ComputeSha256`, `WindowsActionCatalog.TryGet`, `ThemeManager.IsLightTheme`, `LocalizationService.SupportedLanguages`, `LocalTelemetryQueue.PurgeOlderThan`, scaffolding do website) — nada disso reapareceu, remoção consolidada.

**Config/build (`.editorconfig`, `FiveMCleaner.slnx`, `.config/dotnet-tools.json`):** todos LIVE. `NuGet.config` confirmado inexistente (não é dead code, nunca existiu). Testes (`SharedTestDoubles.cs`, `Windows/TestDoubles.cs` com 25 Fakes, `GlobalUsings.cs`, ~30 arquivos `*Tests.cs` com nome "sem match" pelo heurístico ingênuo) — todos LIVE, confirmados falso positivo do heurístico (classe de teste não é referenciada por nome, é descoberta pelo runner).

**Compat shims identificados — NÃO tocar sem investigação dedicada:**
- `AppOptimizationService.DeserializeSettings` (leitura lenient de `settings.json`) — LEGACY-LIVE, motivado por regressão histórica documentada em teste (schema drift resetando opt-out de telemetria).
- `PrivacyConsentPolicy`/`PrivacyConsentVersion` (v2/v3) — LEGACY-LIVE, gate de consentimento versionado, documentado em `docs/telemetry.md`.

### Divergências doc-vs-código (não é dead code, achado à parte)

`docs/graphics-optimizations-backlog.md:385,392` — o resumo final ("Resumo por perfil"/"Próximos passos") não foi atualizado após a "quarta rodada" (26/07/2026) já ter implementado G-SYNC guidance e alerta de driver antigo/Instant Replay/Freestyle (confirmado em `HardwareDiagnosticActions.cs:275-295`). Recomenda-se tarefa de documentação separada para corrigir.

---

## App WPF (Fase 5)

**19 dos 20 itens da lista de partida eram LIVE** (falso positivo: multi-tipo por arquivo, `x:Class` de code-behind, DI por interface). Cross-check adicional (bindings XAML × recursos × propriedades de ViewModel × `.resx` × assets) encontrou um cluster real de dead code, em grande parte correlacionado à reconstrução recente do Otimizador (UI antiga substituída, mas código/recursos remanescentes).

### DEAD-PROVEN confirmados (não removidos nesta fase — ver justificativa abaixo)

| Item | Evidência | Risco |
|---|---|---|
| `Controls/VectorPathIcon.cs` | Zero refs em `.xaml`/`.cs`; sistema de ícones vetoriais próprios nunca adotado (nav-rail usa `ui:SymbolIcon` do Wpf.Ui) | Baixo — arquivo isolado |
| Cluster de geometrias/estilos em `Themes/Icons.xaml` (IconHome, IconOptimizer, IconSettingsGear, IconAccount, IconLogout, IconChevronDown/Right, IconFolder, IconCopy, IconExternalLink, IconCpu, IconGpu, IconMemory, IconDisk, IconNetwork, IconMonitor, IconClock, IconGauge, IconBroom, IconCores, IconSpark, `Icon24`, `VectorIconSmallStyle`, `VectorIconLargeStyle`, `VectorIconCaptionStyle`) | Zero `StaticResource`/`DynamicResource`/`FindResource` | Baixo |
| `AlertRowDangerStyle`, `AlertRowWarningStyle`, `FieldErrorStyle`, `ControlCornerRadius`, `NavigationRailItemStyle` (`Themes/Controls.xaml`) | Zero refs | Baixo |
| `DialogSurface`, `ListRowSurface` (`Themes/Surfaces.xaml`) | Zero refs | Baixo |
| `MonoText` (`Themes/Typography.xaml`) | Zero refs | Baixo |
| `AccentMutedBrush`, `CanvasVignetteBrush`, `RevertBorderBrush`, `RevertSurfaceBrush`, `TextDisabledBrush`, `ToggleTrackBrush` (Colors.Light/Dark.xaml, par) | Zero refs em nenhum dos 2 temas | Baixo |
| `MainViewModel.ActivityLog` + `DisplayModels.ActivityLogItem`, `StepCounterLabel`, `ProgressDetail`, `ProgressStateLabel`, `LightImpactLabel`, `BalancedImpactLabel`, `AggressiveImpactLabel`, `FreeDiskLabel`, `FreeDiskDetail` | Sem `{Binding}` em `OptimizerPage.xaml`/`OverviewPage.xaml` pós-redesign; superseded por `ProgressHeadline`/`ProfilePresentationImpact`/tiles atuais | **Médio** — escritas em ~10 pontos de `MainViewModel.cs`, não é remoção de 1 linha |
| `Window.Minimize/Maximize/Restore/Close`, `Common.Cancel`, `Common.Continue`, `Dashboard.Kpi.Disk.Title` (`.resx`, 3 idiomas) | Zero refs; `TitleBar` do Wpf.Ui não depende dessas strings; tile de Disco removido do Overview redesenhado | Baixo |
| `Assets/FiveMCleaner-master.png` | Não está em nenhum `<ItemGroup>` do `.csproj` — nem entra no build | Baixo |

### SUSPICIOUS (não decidir agora)

- `MotionMicro/Nav/Enter/Structural/Exit`, `EaseMicro/Nav/Enter/Structural/Exit` (`Themes/Tokens/Motion.xaml`) — comentário no arquivo indica scaffolding intencional de design system para Storyboards futuras; só `MotionControl`/`EaseControl` têm consumidor hoje. Não é dead code claro — decisão de produto/design, não de Fase 1.

### Nota metodológica crítica para a Fase 9

A varredura de `.resx` deve reconhecer composição dinâmica de chave (`$"Actions.{id}.Name"`, `$"Profiles.Impact.{profile}.{suffix}"`, `$"Streaming.Check.{kind}.{suffix}"`, `$"Greeting.{period}..."`, `$"Category.{category}"`) — um grep literal por chave geraria ~390 falsos positivos de "chave morta" nas famílias `Actions.*`/`Profiles.*`/`Streaming.Check.*`/`Greeting.*`/`Runtime.*`/`Error.*`/`History.State.*`/`Results.*`/`Update.*`. Confirmado por amostragem representativa de cada família, não por checagem exaustiva das ~390 chaves individualmente.

### Por que nada foi removido nesta fase

Todos os itens acima são reais, mas nenhum atende simultaneamente aos dois critérios exigidos pelo CLEANING.md para remoção na Fase 1 ("DEAD-PROVEN **de risco trivial**"): os itens de XAML/resx isolados são de baixo risco mas pertencem coerentemente ao escopo de limpeza da Fase 5 (junto com o cluster de ViewModel, que por sua vez não é trivial — tem múltiplos call-sites em `MainViewModel.cs`). Misturar uma remoção parcial agora fragmentaria o trabalho da Fase 5 sem necessidade. Fica tudo documentado para execução coesa na Fase 5.

---

## Decisão sobre `ActionReversibility.SessionScoped`

Confirmei que o enum é serializado como string estrita (`JsonStringEnumConverter(..., allowIntegerValues: false)` em `FiveMCleanerJson.cs:51`) e que `Reversibility` flui para o journal transacional persistido localmente (`TransactionJournal.cs:49`). Como o produto já está publicado (v1.3.2, usuários reais) e não há como provar, só por código-fonte atual, que nenhuma versão anterior já persistiu esse valor em disco de usuário, **não removi este enum nesta fase** — não atende à barra de "risco trivial, totalmente desconectado de fases críticas" do CLEANING.md (toca o motor transacional/Fase 3 e compatibilidade de dados persistidos). Fica documentado como candidato para a Fase 4/9 decidir com uma verificação de compatibilidade adequada (ou depreciar com tolerância de deserialização em vez de excluir).

---

## Conclusão da Fase 1

**Critério de conclusão do CLEANING.md** ("as Fases 2–9 precisam ter um mapa de escopo claro e nenhuma área importante do repositório pode ficar fora"): atendido. As 9 áreas de escopo (grafo .NET, Windows Infrastructure/Engine, Action Catalog, App WPF, Broker/IPC, Launcher/Updater/ReleaseTool/installer/scripts/CI, Worker/dashboard/website, testes/config/docs transversais) foram cobertas com evidência concreta (path:line), método declarado e classificação obrigatória.

**Nenhuma remoção de código foi feita nesta fase**, apesar de existirem candidatos DEAD-PROVEN reais (App WPF principalmente, e `ActionReversibility.SessionScoped`). Decisão deliberada: nenhum candidato atendia simultaneamente aos dois critérios exigidos pelo CLEANING.md para remoção na Fase 1 — "risco trivial" **e** "totalmente desconectado das fases críticas" — sem exigir uma verificação ou um conjunto de call-sites que já pertence coerentemente ao escopo de uma fase futura específica. Isso também evitou um ciclo adicional de build/test/validação nesta fase de puro inventário, conforme pedido de contenção de custo do usuário durante a execução.

**Achados que não são dead code, mas relevantes:**
- Fase 0 (`refactor/phase0-contracts-core`) não integrada à `dev/proxima-versao` — reverificar áreas sobrepostas após integração.
- Duplicações semânticas reais (GPU registry read, matcher de processo, safe-XML-write, guarda "jogo fechado", protocolo de eventos do Broker) para consolidar nas respectivas fases.
- 1 achado arquitetural (Inspector que muta estado) para a Fase 3.
- 1 gap operacional no Worker (secret não documentado) e 1 migration D1 legada para a Fase 8.
- Divergências de documentação (`docs/graphics-optimizations-backlog.md`, comentário obsoleto em `schema.sql`) para limpeza de docs.
- Inconsistência de conteúdo pt/en na FAQ do site sobre telemetria — já sinalizada como task separada pelo agente responsável, fora do escopo de dead code.

**Validação executada:** nenhuma validação de build/teste .NET foi necessária, pois nenhum código foi alterado nesta fase — apenas este relatório foi criado. `git diff --check` será executado sobre o commit final antes do PR.

**Próximo passo:** integrar/revisar este relatório, decidir separadamente sobre a integração da Fase 0, e então iniciar a Fase 2 (Windows Infrastructure) em uma tarefa isolada nova, partindo deste mapa.
