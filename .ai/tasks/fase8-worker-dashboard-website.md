# Fase 8 — Worker, dashboard e website

## Resultado

- Revalidado o grafo de rotas do Worker contra clientes .NET e dashboard; handlers, builders, módulos e assets possuem consumidores vigentes.
- Documentado `RELEASE_MANIFEST_JSON` no README e no inventário de secrets do Wrangler, incluindo sua origem no workflow oficial de release estável e a proibição de commitar ou redigir manualmente o valor.
- Removido do snapshot D1 o comentário obsoleto que ainda descrevia `attachment_key` e R2; a tabela e o fluxo atuais são text-only.
- Preservada a migration `0001_account_username_terms.sql`: migration aplicada é histórico operacional, não código descartável, e qualquer remoção remota exige autorização/deploy separado.
- A divergência de texto da FAQ pt/en permanece fora deste escopo e a página não foi tocada, preservando também o trabalho concorrente existente no checkout compartilhado.

## Classificação dos achados

| Item | Decisão |
| --- | --- |
| Rotas, módulos e assets Worker/dashboard/site | `LIVE` |
| `RELEASE_MANIFEST_JSON` | `LIVE`; gap operacional de documentação corrigido |
| Comentário de R2/`attachment_key` | `DEAD-PROVEN`; removido |
| Migration D1 legada | `LEGACY-LIVE`; preservada |
| Inconsistência da FAQ | achado independente; não é dead code |

## Validação

- `npm test` — aprovado, 165/165 testes do Worker.
- `npm audit` — aprovado, 0 vulnerabilidades.
- `git diff --check` — aprovado.
- Nenhum deploy, alteração de secret ou operação D1 remota foi executado.
