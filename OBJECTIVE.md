# Objetivo da tarefa

- **Agente**: Claude Code
- **Objetivo**: reduzir duplicação, complexidade e fragmentação de
  responsabilidades no código .NET do Ralven, sem alterar comportamento
  funcional, contratos persistidos, segurança, rollback ou limites de
  privilégio.

## Escopo

Pertence à tarefa:

- consolidar ações de registro HKCU quase idênticas em `Ralven.Windows`;
- unificar regras duplicadas do arquivo de configurações gráficas do
  GTA V/FiveM entre as duas ações que o editam;
- unificar as duas comparações de preset gráfico que diferem apenas pela
  direção;
- remover o pipeline de recuperação duplicado nos dois `catch` da execução
  isolada do motor transacional;
- eliminar o P/Invoke de memória duplicado em `Ralven.App` reutilizando o
  inspetor já existente em `Ralven.Windows`;
- fazer `CloudflareAccountProfileService` usar o transporte compartilhado já
  adotado pelos demais serviços Cloudflare;
- centralizar a resolução "string localizada com fallback", hoje repetida em
  quatro arquivos da camada de apresentação;
- dividir `AppOptimizationService` em `partial class` por responsabilidade,
  seguindo a convenção já usada em `MainWindow`/`MainViewModel`.

Não pertence à tarefa:

- mudança de comportamento observável, de mensagens ao usuário, de contratos
  persistidos (journal, snapshot, settings, plano do broker) ou de invariantes
  de segurança/rollback;
- reformatação massiva, renomeações cosméticas ou novas abstrações
  especulativas;
- alteração de superfícies remotas (Worker, dashboard, site) ou do fluxo de
  instalador/updater.

## Critérios de conclusão

- build Release sem avisos novos;
- suíte .NET com o mesmo número de testes aprovados do baseline (1.432) ou
  superior, sem testes enfraquecidos ou removidos;
- `dotnet format --verify-no-changes` limpo;
- `scripts\Verify-Safety.ps1` aprovado;
- diff revisado, restrito ao escopo acima.

## Resultado entregue

A preencher ao concluir.
