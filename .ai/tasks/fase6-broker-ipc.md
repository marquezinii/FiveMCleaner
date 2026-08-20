# Fase 6 — Broker e IPC

## Resultado

- Consolidado em `FiveMCleaner.Contracts` o contrato tipado de eventos do broker, antes duplicado no executável privilegiado e dentro do cliente WPF.
- App e Broker agora serializam e desserializam o mesmo `BrokerEvent`/`BrokerEventKind`, preservando nomes, ordem e tipos das propriedades, enumeração textual e `SchemaVersion = 1`.
- O campo `ErrorCode` não foi removido do protocolo: embora o cliente não o interprete, o Broker o emite em falhas e as opções JSON estritas exigem que o receptor reconheça o membro. A cópia morta desapareceu junto com o DTO duplicado.
- Preservado o overload de `ResolveAdministratorActions(IEnumerable<(string Id, int Version)>)`: o teste existente comprova que ele é um gate direto contra a ampliação de privilégio e, portanto, é `LIVE`.
- Nenhum comando genérico, acesso de rede ou enfraquecimento de validação foi introduzido no Broker.

## Classificação dos achados

| Item | Decisão |
| --- | --- |
| DTO/enum de evento duplicados | `DUPLICATE`; consolidados em Contracts |
| `BrokerEventWire.ErrorCode` isolado no App | removido com a cópia; membro preservado no contrato canônico por uso do emissor |
| Resolver administrativo por pares ID/versão | `LIVE`; gate de segurança coberto por teste |
| Demais tipos do Broker/IPC | `LIVE`; preservados |

## Validação

- Adicionado teste de round-trip para estado, código de erro e IDs aplicados no contrato canônico.
- `dotnet build FiveMCleaner.slnx --configuration Release --no-restore` — aprovado, 0 avisos e 0 erros.
- `dotnet test FiveMCleaner.slnx --configuration Release --no-build` — aprovado, 820/820 testes.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` — aprovado.
- `scripts/Verify-Safety.ps1` — aprovado.
- `git diff --check` — aprovado.
