# Startup Audit & Fix

- **Agente:** opencode
- **Branch:** `ai/opencode/startup-audit-fix`
- **Base:** `dev/proxima-versao` (commit `4c8b6b5`)
- **Objetivo:** Corrigir bugs e gargalos de performance na startup do aplicativo (cold start e warm start)
- **Status:** Pronto para integração

## Mudanças

### Bugs corrigidos

1. **Race condition em `StreamingSoftwareDetector.CollectInstalledProductNames`** — 4 `Task.Run` paralelos escreviam na mesma `List<string>` sem sincronização. Cada probe agora recebe sua própria lista e os resultados são unificados após `WaitAll`.

2. **`FirebaseAuthService.Fail()` fire-and-forget inseguro** — `_ = LogoutAsync()` podia gerar unobserved task exception. Agora usa `.ContinueWith` para observar a exceção em caso de falha.

3. **Double dispose de `accountService` em `MainWindow_Closed`** — Removida a chamada duplicada (`(accountService as IDisposable)?.Dispose()`); restou apenas `accountService?.Dispose()`.

4. **`LoadHistoryAsync` não capturava `NotSupportedException`** — Um journal com schema incompatível derrubava o carregamento inteiro. Agora captura tanto `JsonException` quanto `NotSupportedException`.

### Performance

5. **`RestoreSessionAsync` não bloqueia mais a startup** — A restauração de sessão Firebase agora é fire-and-forget. O evento `StateChanged` já existente atualiza a UI quando completar. Elimina até 40s de bloqueio sequencial em rede lenta.

6. **Cache de `PerformanceCounterCategory.Exists("GPU Engine")`** — Operação conhecida por ser lenta (500-3000ms) agora é cacheada estaticamente após a primeira consulta.

### Formatação

7. **`dotnet format` corrigiu indentação pré-existente** em `AppOptimizationService.cs` e `LiveSystemMetricsProvider.cs` (mudanças puramente de whitespace, sem alteração de lógica).

## Arquivos alterados

- `src/FiveMCleaner.App/Services/StreamingSoftwareDetector.cs`
- `src/FiveMCleaner.App/Services/FirebaseAuthService.cs`
- `src/FiveMCleaner.App/Services/AppOptimizationService.cs`
- `src/FiveMCleaner.App/MainWindow.xaml.cs`
- `src/FiveMCleaner.Windows/Infrastructure/ResourceUsageInspector.cs`
- `src/FiveMCleaner.App/Services/LiveSystemMetricsProvider.cs` (apenas whitespace)

## Validação

- Build Release: 0 avisos, 0 erros
- Testes .NET: 690 aprovados, 0 falhas
- `dotnet format --verify-no-changes`: aprovado
- `git diff --check`: limpo
