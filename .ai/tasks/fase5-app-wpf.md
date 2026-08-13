# Fase 5 — App WPF e recursos

## Resultado

- Removido o estado do `MainViewModel` que era atualizado, mas nunca consumido por XAML, código ou testes: log visual, detalhes redundantes de progresso, contador textual de etapas, rótulos de impacto por perfil e KPI de disco livre.
- Removidos o controle `VectorPathIcon`, a imagem mestre não empacotada e recursos XAML sem consumidor comprovado em ícones, controls, surfaces, tipografia e paletas clara/escura.
- Removidas, de forma simétrica nos três idiomas, as chaves de localização cujo único consumidor era o estado morto, incluindo mensagens `Log.*`.
- Preservados os tokens de motion classificados como suspeitos, os recursos referenciados dinamicamente e toda a apresentação efetivamente vinculada.
- A execução continua expondo progresso real por percentual, headline, ledger de etapas e relatório final; nenhuma operação de sistema, política ou contrato persistido foi alterado.

## Classificação dos achados

| Item | Decisão |
| --- | --- |
| `ActivityLog` e `ActivityLogItem` | `DEAD-PROVEN`; sem binding ou leitor |
| Propriedades de progresso/impacto/KPI não vinculadas | `DEAD-PROVEN`; escritas sem consumidor |
| `VectorPathIcon` e imagem mestre | `DEAD-PROVEN`; sem referência e sem empacotamento |
| Ícones, styles, brushes e textos órfãos | `DEAD-PROVEN`; busca por chave sem consumidores |
| Tokens `Motion.*` | `SUSPICIOUS`; preservados por uso potencial em recursos dinâmicos |

## Validação

- `dotnet build FiveMCleaner.slnx --configuration Release --no-restore` — aprovado, 0 avisos e 0 erros.
- `dotnet test FiveMCleaner.slnx --configuration Release --no-build` — aprovado, 819/819 testes.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` — aprovado após normalização dos finais de linha.
- `scripts/Verify-Safety.ps1` — aprovado.
- `git diff --check` — aprovado.
- As três tabelas RESX permanecem com o mesmo conjunto de chaves.
