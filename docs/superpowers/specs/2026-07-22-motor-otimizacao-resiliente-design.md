# Motor de otimização resiliente, progresso, relatório e apresentação de modos

Data: 2026-07-22
Autor: agente (Claude) — handoff multi-IA
Status: aprovado pelo usuário para implementação

## Contexto

O FiveMCleaner já implementa, no código atual:

- seleção **somente por modo** (Leve/Médio/Agressivo); as opções técnicas são
  derivadas do perfil em `MainViewModel.RefreshPlan()`, sem checklist de tweaks;
- **verificação antes de modificar**: cada ação retorna
  `WindowsActionApplyResult.NoChange(...)` quando o sistema já está correto;
- motor transacional com journal, snapshot, quarentena, rollback por ação,
  broker elevado de escopo mínimo e guardas de edição/processo/energia;
- progresso com porcentagem, barra, tempo decorrido e estimativa de tempo
  restante por média móvel; pesquisa técnica sourced em `docs/research.md`.

Esta especificação cobre apenas as lacunas reais em relação ao PROMPT 2, sem
reescrever nem degradar o que já funciona.

## Objetivos

1. Substituir o modelo de execução "tudo ou nada" por **execução isolada por
   ação**, preservando o rollback atômico interno de cada ação.
2. Fornecer **progresso estruturado**: etapa X de N e um livro-razão de
   resultados por etapa (verificado/alterado/ignorado/aviso/falha).
3. Produzir um **relatório final estruturado** e um **relatório técnico
   copiável e sanitizado** para suporte.
4. Melhorar a **apresentação de cada modo** (benefícios, impacto, riscos,
   reversibilidade, categorias analisadas, aviso honesto de variação).
5. Tornar a **documentação por ação** de primeira classe (versões do Windows
   suportadas, como detectar, como aplicar, como confirmar, como desfazer,
   riscos/limitações).
6. Testes, atualização de documentação (`safety.md`, `architecture.md`,
   `PROJECT_STATE.md`) e commits lógicos.

## Não objetivos

- Não inventar novas otimizações nem alterar o allowlist do broker.
- Não enfraquecer proteções do Windows, não prometer FPS, não adicionar
  telemetria.
- Não reescrever diagnóstico, streaming readiness, updater, broker ou
  localização, exceto onde consomem os novos dados de resultado/relatório.

## Modelo de execução isolada por ação

### Contrato de resultado

Novo enum em `FiveMCleaner.Contracts`:

```
ActionExecutionOutcome:
  Verified        // já estava correto (NoChange), nenhuma escrita
  Applied         // alteração aplicada e confirmada
  Skipped         // pré-condição/opção/caminho ausente — sem erro
  Warning         // aplicado com ressalva, ou sucesso parcial reportável
  Failed          // erro genuíno; a própria ação foi revertida
  RolledBack      // revertida com sucesso após falha
  RollbackFailed  // reversão falhou — exige atenção
  Blocked         // edição/segurança não suportada
  NotRun          // não executada (run abortada por falha crítica anterior)
```

### Metadados de ação (Core)

`OptimizationActionDefinition`/`ActionMetadataDto` ganham:

- `Prerequisites: IReadOnlyList<string>` — IDs de ações cujo sucesso é
  condição para executar esta ação;
- `IsCritical: bool` — se falhar, a run aborta as ações independentes
  restantes (elas viram `NotRun`);
- `SupportedWindows` — flags `Windows10`, `Windows11`;
- `DetectionSummary`, `ConfirmationSummary`, `UndoSummary`,
  `RiskLimitations` — documentação por ação.

As ações de verificação de segurança (`VerifyFiveMIsStopped`,
`VerifyGtaVIsStopped`) tornam-se `IsCritical` e prerequisito das ações de
escrita correspondentes (limpezas/gráficos que exigem processo encerrado).

### Laço de execução (WindowsTransactionEngine)

Sequencial, por ação, na ordem do plano:

```
para cada ação:
  se run abortada           -> NotRun
  senão se prereq não teve sucesso (Verified/Applied) -> Skipped(dependency)
  senão:
    tenta:
      apply  (verifica estado interno; pode NoChange)
      se changed: commit
      registra outcome: NoChange->Verified, changed->Applied
    captura exceção:
      rollback SOMENTE desta ação (atômico interno já existente)
      registra Failed ou RollbackFailed
      se ação.IsCritical: marca abort -> restantes independentes viram NotRun
      senão: continua
```

- Cada ação vira uma mini-transação (apply→commit inline). A ordenação por
  irreversibilidade deixa de ser global e passa a ser irrelevante para
  isolamento, pois uma falha reverte apenas a própria ação.
- O journal continua registrando cada etapa; `WindowsActionJournalState`
  ganha `Verified` e `Skipped` para preservar histórico e rollback manual.
- A run **só é reportada como totalmente bem-sucedida se não houver
  `Failed`/`RollbackFailed`**. Se houver, o resultado é `CompletedWithErrors`.
- Falhas críticas que tornem inseguro continuar (ex.: verificação de segurança
  que não confirma o estado do processo, falha de escrita do journal) abortam
  a run com `NotRun` para o restante.

### Compatibilidade

O modo estrito atual (`RollbackOnFailure`) é preservado como opção do
`WindowsTransactionOptions` para os fluxos que ainda dependem dele
(ex.: rollback manual). O caminho de execução principal do app passa a usar o
modo isolado. Os testes existentes de rollback total continuam válidos para o
modo estrito.

## Progresso estruturado

- `AppProgressUpdate` ganha `CompletedSteps`, `TotalSteps` e `Outcome?`.
- `WindowsActionProgress` ganha índice de etapa e total de etapas.
- A `MainViewModel` mantém `ObservableCollection<StepLedgerItem>`: uma linha por
  ação com ícone, nome localizado, estado (Verificado/Alterado/Ignorado/
  Aviso/Falha) e cor. Uma contagem viva (tally) resume os estados.
- Estimador de tempo, barra e porcentagem existentes são preservados.

## Relatório final e cópia para suporte

- Novo `OptimizationReportDto` construído a partir do journal: contagens
  (verificado/alterado/ignorado/aviso/falha), `RequiresRestart`,
  `RestorePossible`, `TransactionId`, linhas por ação.
- View de resultados exibe o relatório; botão **"Copiar relatório técnico"**
  escreve texto simples **sanitizado** na área de transferência.
- `TechnicalReportBuilder` + `ReportSanitizer`: remove nome de usuário de
  caminhos (`C:\Users\<user>\` -> `%USERPROFILE%\`, idem `%LOCALAPPDATA%`,
  `%APPDATA%`), nunca inclui tokens, entitlement, cookies ou conteúdo pessoal.
  Coberto por testes.

## Apresentação de modos

- Novo tipo em Core `OptimizationProfilePresentation` com: `Description`,
  `Benefits`, `ImpactLevel`, `Risks`, `Reversibility`, `AnalyzedCategories`
  (derivadas do catálogo para o perfil), `VariabilityNote`.
- A `MainViewModel` expõe a apresentação do modo selecionado; o XAML mostra o
  bloco estruturado. As categorias analisadas derivam das ações reais do
  perfil, evitando divergência.
- Strings localizadas em pt-BR e inglês.

## Documentação por ação

- Metadados de doc (acima) preenchidos no catálogo e localizados.
- A revisão de plano ("Review plan") passa a exibir, por ação: o que faz, por
  que é útil, versões do Windows, como é detectada, como é confirmada, como
  desfazer, riscos/limitações.
- Ações são filtradas por `SupportedWindows` conforme a versão detectada do
  Windows (10/11), com teste.

## Testes

xUnit novos/atualizados:

- classificação de outcome (Verified/Applied/Skipped/Warning/Failed);
- skip por dependência (prereq falhou -> Skipped);
- abort por falha crítica (restantes -> NotRun);
- isolamento de rollback (uma ação falha, as outras sobrevivem);
- "não é sucesso total se qualquer ação falhou";
- construção do relatório e contagens;
- sanitização do relatório técnico (sem nome de usuário, sem segredos);
- contagem de etapas X de N;
- derivação do bloco de apresentação do modo;
- gating por versão do Windows.

Testes que alterariam o Windows real permanecem opt-in; a suíte usa doubles e
diretórios temporários.

## Documentação a atualizar

- `docs/safety.md`: documentar o modelo isolado, invariantes que continuam
  valendo (rollback atômico por ação, sem sucesso parcial reportado como total,
  cancelamento seguro), e o comportamento de dependência/criticalidade.
- `docs/architecture.md`: modelo de execução por ação e novos estados.
- `PROJECT_STATE.md`: funcionalidades entregues, decisões e validação, com
  handoff para a próxima IA.

## Plano de commits (lógico)

1. Contracts + Core: enum de outcome, metadados de ação, apresentação de modo.
2. Windows engine: execução isolada, dependências/criticalidade, estados de
   journal, relatório a partir do journal.
3. App + UI: progresso estruturado, livro-razão, relatório final, cópia
   sanitizada, apresentação de modos, doc por ação; strings localizadas.
4. Testes e documentação (safety/architecture/PROJECT_STATE).

Commits podem ser agrupados se a coesão pedir, sem forçar um único commit
gigante.
