# Function Decomposition no projeto inteiro

- **Agente:** opencode
- **Branch:** `ai/opencode/function-decomposition`
- **Objetivo:** rodada completa de Function Decomposition (dividir funções gigantes) preservando comportamento.
- **Status:** pronto para integração.

## Resumo das mudanças

Rodada de Extract Method em 8 arquivos, um commit por fase (rastreabilidade). Nenhuma mudança de comportamento: ordem de validação, tipos/mensagens de exceção, persistência de journal, relatório de progresso e wire-up de serviços preservados. Nenhuma abstração nova além de dois pequenos records privados de contexto no `MainWindow` (agrupamento legítimo de outputs relacionados — sem ref/tuplas artificiais).

## Commits

1. `7bd924b` `refactor: decompose WindowsTransactionEngine`
   - `ExecuteAsync` → `ValidateExecutionRequest`, `LoadOrCreateJournalAsync`, `BuildSelectedActions`, `CompleteWithNoSelectedActionsAsync`, `ApplyAndCommitAsync`, `HandleExecutionFailureAsync`.
   - `ExecuteIsolatedAsync` → `SkipRemainingAfterAbortAsync`, `SkipDueToUnmetPrerequisiteAsync`, `BeginItemApplicationAsync`, `ApplyIsolatedItemAsync`, `AbortRemainingEntries`, `ReportOutcomeFor`, enum `IsolatedItemResult`.
2. `f670c62` `refactor: decompose AppOptimizationService`
   - `DiagnoseAsync` → `BuildDiagnosticNotices`.
   - `ExecutePlanCoreAsync` → `ReportPreparing`, `ExecuteLocalPhaseAsync`, `ExecuteElevatedPhaseAsync`, `LoadFinalRunSucceededAsync`, `ReportCompletion`, `CaptureComparisonAsync`.
   - `RollbackCoreAsync` → `ExecuteElevatedRollbackAsync`.
3. `b34f2df` `refactor: decompose MainViewModel`
   - `StartOptimizationAsync` → `TryPrepareOptimizationRun`, `HandleOptimizationResultAsync`, `HandleOptimizationCancelled`, `HandleOptimizationFailed`, `FinalizeOptimizationRun`.
4. `e541268` `refactor: decompose GitHubReleaseUpdateService`
   - `ParseManifest` → `ValidateReleaseKind`, `ParseStableVersion`, `GetAssetsProperty`, `SelectInstallerAsset`, `ParseReleaseNotes`. Ordem exata de validações e short-circuiting preservados.
5. `7cd6b1b` `refactor: decompose PlanBuilder`
   - `CreateNotices` → grupos por tema: `CreateRemovalNotices`, `CreatePowerAndProcessNotices`, `CreateRepairNotices`, `CreateGraphicsNotices`, `CreateProfileNotices` (concatenação preserva ordem de emissão).
6. `41b6b89` `refactor: decompose ActionCatalog`
   - `CreateActions` (~1068 linhas) dividida em 8 builders: `CreateVerificationAndBottleneckActions`, `CreateHardwareDiagnosisActions`, `CreateGraphicsDiagnosisActions`, `CreateCleanupActions`, `CreateGamingAndPowerActions`, `CreateGraphicsPresetActions`, `CreateGamingGuidanceActions`, `CreateAppearanceActions`. Ordem exata das 61 definições preservada via concatenação `..`.
7. `2a5e366` `refactor: decompose WindowsOptimizationRuntime`
   - `CreateCatalogActions` (~200 linhas) dividida em 5 builders: `CreateDiagnosticActions`, `CreateCleanupActions`, `CreateRegistryAndPowerActions`, `CreateGraphicsPresetActions`, `CreateVisualEffectsActions`.
8. `9504c11` `refactor: decompose MainWindow constructor`
   - Construtor (~100 linhas) → `ParseCommandLine`, `CreateStartupRegistrationService`, `CreateReleaseUpdateService`, `CreateSilentUpdateInstaller`, `CreateAccountService`, `CreateTelemetryServices`. Dois records privados de contexto: `MainWindowCommandLine`, `MainWindowTelemetry`.

## Testes executados

- Build Release `dotnet build FiveMCleaner.slnx -c Release`: 0 avisos, 0 erros (em cada fase).
- `dotnet test -c Release`: **725/725 aprovados** (em cada fase e na validação final).
- `dotnet format --verify-no-changes`: aprovado (fases 6/7 exigiam normalização CRLF pós-script Python; corrigido).
- `git diff --check`: limpo.
- `Verify-Safety.ps1`: aprovado (compila e roda a suíte de testes).

## Decisões / observações

- **Sem alteração de comportamento**: as validações de segurança do `GitHubReleaseUpdateService` mantêm ordem, exceções e mensagens idênticas; os loops transacionais do `WindowsTransactionEngine` preservam estados de journal, progresso e rollback.
- **Sem ref/tuplas artificiais**: nenhuma extração foi forçada com `ref`/`out` ou tuplas improvisadas; onde houve múltiplos outputs relacionados usou-se parâmetros normais ou dois pequenos records privados de contexto no `MainWindow`.
- **Fábricas de dados**: nenhuma nova abstração/classe foi introduzida; apenas builders privados por categoria preservando a ordem original (pronto para uma futura rodada de "dividir classes gigantes").
- **Ajustes de nulabilidade**: `MainViewModel` ganhou `currentPlan!`/`operationCancellation!` com comentário explicando a invariante garantida pelo guard (`TryPrepareOptimizationRun`) e pela atribuição antes do `try` — a análise de fluxo não enxerga através da extração.
- **`StreamingSoftwareDetector`** não foi alterado: já está bem decomposto (coletas por método, cache de registro, paralelismo em `Detect`).

## Pronto para integração

Branch `ai/opencode/function-decomposition` com 8 commits, cada um testado isoladamente e a suíte completa validada no estado final. Sem merge automático em `dev/proxima-versao`.
