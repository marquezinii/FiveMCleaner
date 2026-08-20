# Fase 2 — Windows Infrastructure

## Resultado

- Consolidada a leitura de adaptadores em `GpuAdapterRegistryReader`; os inspectors de vendor e detalhes continuam read-only e preservam ordenação, deduplicação, VRAM best-effort e retorno vazio quando o Registro não está acessível.
- Consolidada a inspeção defensiva de nome, imagem executável e estado de resposta em `ProcessInspection`, sem transformar a primitiva em executor genérico.
- Separada a mutação do processo travado: `IStuckFiveMProcessInspector` agora apenas observa e `IFiveMProcessTerminator` executa o encerramento.
- O terminator revalida PID, nome, imagem dentro da instalação canonicalizada e estado não responsivo imediatamente antes de `Kill`, evitando agir sobre observação obsoleta ou PID reutilizado.

## Classificação dos achados

| Item | Decisão |
| --- | --- |
| Leitura duplicada de GPU via Registro | `DUPLICATE` consolidado |
| Inspeção duplicada de processos | `DUPLICATE` consolidado nas primitivas semanticamente idênticas |
| `TryTerminate` dentro de Inspector | `MISPLACED` corrigido com separação leitura/mutação |
| Demais inspectors/locators/readers | `LIVE`; preservados |

## Validação

- `dotnet build FiveMCleaner.slnx --configuration Release --no-restore` — aprovado, 0 avisos e 0 erros.
- `dotnet test FiveMCleaner.slnx --configuration Release --no-build` — aprovado, incluindo três testes novos da ação de encerramento.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` — aprovado.
- `scripts/Verify-Safety.ps1` — aprovado.
- `git diff --check` — aprovado.
