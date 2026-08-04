# Single-instance activation — opencode

- Agent: opencode
- Branch/worktree: `ai/opencode/single-instance-activation` (`C:\Projetos\FiveMCleaner-ai-opencode-single-instance`)
- Objective: ao tentar abrir uma segunda instância, o app não abre uma duplicata — sinaliza a instância em execução, que traz sua janela para o primeiro plano
- Status: **pronto para integração**

## Summary

Substituiu o comportamento do segundo processo (que antes mostrava um MessageBox "já está em execução" e saía) por sinalização de ativação silenciosa.

### `src/FiveMCleaner.App/Services/SingleInstanceGuard.cs`

- Adicionou `EventWaitHandle` nomeado auto-reset por ambiente (`Local\FiveMCleaner.SingleInstance.<env>.Activate`), criado logo após vencer o `Mutex` para não perder requisições que chegam antes do listener subir.
- `RequestActivation()`: chamado pela instância perdedora; abre/sinaliza o evento do dono.
- `ListenForActivation(Action)`: thread de background que observa o evento e invoca o callback (que precisa fazer marshaling para a thread de UI).
- `BuildActivationEventName(environment)` internal para testes; escopo por ambiente (Dev vs Prod) mantido; `UnauthorizedAccessException`/`IOException` tratados de forma não-crashing como no guard original.

### `src/FiveMCleaner.App/App.xaml.cs`

- Instância perdedora: `RequestActivation()` + `Dispose()` + `Shutdown(0)` silencioso (sem diálogo).
- Instância vencedora: `ListenForActivation(OnActivationRequested)`, com marshaling via `Dispatcher.BeginInvoke` para `MainWindow.RequestActivation()`.
- Removido `ShowAlreadyRunningMessage()` e o recurso `Dialog.AlreadyRunning.Message`.

### `src/FiveMCleaner.App/MainWindow.xaml.cs`

- Extraído `RequestActivation()` (mostra/restaura/ativa a janela + `SetLiveMetricsEnabled`) a partir de `TrayIcon_ShowRequested`, que agora delega para ele — mesmo caminho usado pelo pedido de ativação.

### Recursos e testes

- Removida a chave `Dialog.AlreadyRunning.Message` dos três catálogos (`Strings.resx`, `Strings.pt-BR.resx`, `Strings.es.resx`) — o teste de paridade de chaves en/pt continua passando.
- `tests/FiveMCleaner.Tests/App/SingleInstanceGuardTests.cs`: novos testes para nome do evento de ativação (por ambiente e diferença Dev/Prod) e para a sinalização chegar ao listener (antes e depois do listener estar ativo — propriedade "sem perda de requisição").

## Validation

| Check | Result |
| --- | --- |
| `dotnet build FiveMCleaner.slnx -c Release` (via Verify-Safety) | 0 errors / 0 warnings |
| `dotnet test` Release | 641 passed (inclui 4 novos de ativação + paridade resx) |
| `dotnet format --verify-no-changes` | passed |
| `scripts/Verify-Safety.ps1` | passed |

## Decisions / left alone

- Modo demo (`--demo`/`--demo-synthetic`) continua isento do guard.
- Escopo por `AppRuntimeEnvironment` mantido (Dev e Prod lado a lado continua sendo fluxo legítimo do `Start-DevelopmentApp.ps1`).
- Sinal de ativação é por evento nomeado (não por named pipe): simples, auto-reset não perde requisições e sobrevive ao processo; não há troca de dados além do "traga sua janela".
- Não tocou em outros worktrees/branches; nenhum push/remote feito.

## Commits

- Único commit local nesta branch (ver git log).
