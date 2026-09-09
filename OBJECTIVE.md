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

Sete consolidações, nenhuma mudança de comportamento observável:

- `GameBooleanRegistryAction` passa a concentrar as regras que
  `GameModeRegistryAction` e `GameDvrRegistryAction` repetiam integralmente;
- `GraphicsSettingsFile` concentra o nome de arquivo aceito por alvo e a
  sondagem de arquivo inexistente, antes duplicados entre
  `DisplayPreferencesAction` e `LegacyGraphicsPresetAction`;
- `ShouldLowerValue`/`ShouldRaiseValue` viram `ShouldChangeValue`, com teste
  que fixa a simetria das duas direções;
- `RecoverIsolatedItemAsync` remove o pipeline de recuperação duplicado nos
  dois `catch` de `ApplyIsolatedItemAsync`, preservando a gravação prévia do
  journal exclusiva do caminho de falha;
- `NativeMemoryStatus` foi removido: o diagnóstico usa o
  `WindowsSystemResourceInspector` já existente, tirando P/Invoke da camada de
  apresentação;
- `CloudflareAccountProfileService` passa a usar `CloudflareTransportDefaults`
  como os outros seis serviços Cloudflare;
- `LocalizationFallback.GetStringOrFallback` substitui quatro implementações
  privadas e seis repetições inline em `ToDisplayItem`;
- `AppOptimizationService` foi dividido em `partial class` por
  responsabilidade (principal, `Settings`, `Diagnostics`, `History`), sem
  adicionar, remover ou alterar nenhum membro.

Validação executada na branch:

- `dotnet build Ralven.slnx --configuration Release`: sucesso, 0 avisos;
- suíte .NET: **1.434 testes aprovados**, 0 falhas (baseline era 1.432; os
  2 novos cobrem a simetria das direções do preset gráfico);
- `dotnet format Ralven.slnx --verify-no-changes`: limpo;
- `scripts\Verify-Safety.ps1`: aprovado;
- `git diff --check`: limpo;
- `scripts\Install-DevelopmentShortcut.ps1 -Build`: atalho reconstruído.

Limitação real: o SharpLens MCP citado em `CLAUDE.md` não estava conectado
nesta sessão, então a análise semântica foi feita com compilador, LSP local,
busca estrutural e a suíte de testes, e não com `find_references`/
`get_call_graph`.
