# Auditoria e expansão de códigos de erro — design

Data: 2026-09-03

## Objetivo

Hoje o Ralven já tem um enum `BugCode` (`Ralven.Contracts`) bem populado e um
`BugCodeClassifier` que mapeia exceções para códigos, mas isso serve quase só
o backend de telemetria. O usuário final nunca vê um código de erro na tela; o
dashboard mostra o código cru (`APP_OPT_ACTION_EXECUTION`) sem nome nem
contexto; e só a *run* inteira de otimização é classificada, não cada ação
individual. Esta mudança:

1. Audita e expande o enum `BugCode` para cobrir partes do app sem código hoje
   (cobrança, inventário de Aplicativos, saúde do Sistema) e preenche lacunas
   nas categorias existentes.
2. Dá um nome amigável (pt-BR/en/es) a cada código, reaproveitando o mecanismo
   de localização existente.
3. Classifica falhas por **ação individual** do plano, não só a run inteira —
   é o que mais ajuda a achar a causa raiz de um problema específico.
4. Mostra o código ao usuário no padrão comum de apps grandes: mensagem
   humana + código discreto ao lado (`"Não foi possível aplicar X. Código do
   erro: BRK_ACTION_EXECUTION"`), sem badge ou selo novo na UI.
5. Dá ao dashboard um nome amigável ao lado do código cru, para achar a causa
   raiz de um bug sem decorar o enum.

## Estado atual (achados da auditoria)

- `BugCode.cs`: ~110 valores, categorias `APP_`, `UPD_`, `BRK_`, `NET_`,
  `FIVEM_`, `GTAV_`, `WIN_`, `CFG_`, `SYS_`. Contrato durável (append-only).
- `BugCodeClassifier` (hoje em `Ralven.App.Services`) só é chamado em 4
  lugares: `Ralven.Launcher/Program.cs`, `MainViewModel.Progress.cs`,
  `SignedManifestUpdateService.cs`, `AtomicUpdateInstaller.cs`. Broker, auth,
  settings e diagnóstico caem no fallback genérico.
- `WindowsActionJournalEntry.OutcomeReason` (string) já existe e é preenchido
  em `WindowsTransactionEngine` a partir da mensagem da exceção; não existe
  `BugCode` por ação.
- `OptimizationReportBuilder`/`OptimizationReportLineDto` (relatório
  estruturado, `Ralven.Windows`/`Ralven.Contracts`) já carregam `Reason` por
  linha; não carregam `BugCode`.
- Nenhuma tela do app exibe `BugCode` hoje. A única exposição é o texto
  copiável do relatório de bug manual (`"Bug code: {0}"`).
- Worker: `bugCodes.js` mantém manualmente (sem geração automática) a lista
  allowlisted em paralelo ao enum C# — convenção já existente, que este
  trabalho mantém. Migration `0008_bug_report_code.sql` já adiciona
  `bug_code` a `bug_reports`/`telemetry_events` e já está integrada em
  `dev/proxima-versao` (implantação em produção é operação separada, fora
  deste escopo).
- Dashboard: `charts.js` `toBugReportRow` já lista `row.bug_code` cru, sem
  nome nem legenda.

## Branches não integradas — coordenação

- `feat/dashboard-insights` reescreve `rendering.js`/`charts.js` (gráficos
  interativos) e adiciona uma query `bugCodeBreakdown` no Worker, limitada ao
  evento `optimization-failed`. Não toca nomes amigáveis.
- `fix/optimization-failure-reporting` é pequena e ortogonal: só garante que
  um caminho de falha específico grave `OutcomeReason`.

Esta tarefa parte de `dev/proxima-versao` (não dessas branches) e evita tocar
o núcleo de `rendering.js`/`charts.js` para minimizar conflito quando
`feat/dashboard-insights` integrar. Isso é comunicado no PR como dependência
de coordenação, não resolvido aqui.

## 1. Relocação do `BugCodeClassifier`

Classificar falhas por ação exige chamar o classifier de dentro de
`WindowsTransactionEngine` (`Ralven.Windows`). Hoje ele vive em
`Ralven.App.Services`, e `Ralven.Windows` não referencia `Ralven.App` (nem
deveria — inverteria a direção de dependência definida em
`docs/architecture.md`). `Ralven.Windows` já referencia `Ralven.Contracts` e
`Ralven.Core`, onde `BugCode` já vive.

Ação: mover `BugCodeClassifier.cs` de `Ralven.App.Services` para
`Ralven.Windows` (namespace `Ralven.Windows.Diagnostics`), sem alterar seu
comportamento. Atualizar os 4 `using`/chamadores existentes (`Ralven.App`,
`Ralven.Launcher` já dependem de `Ralven.Windows` transitivamente, então
continuam funcionando). Isso não é refatoração oportunista: é pré-requisito
para o item 3.

## 2. Expansão do `BugCode`

Append-only, seguindo a convenção de prefixo por domínio já existente:

- Preencher lacunas reais: `APP_OPT_ACTION_NOT_FOUND` (ação referenciada no
  plano não existe mais no catálogo), `WIN_GAMES_SETTINGS` (Modo de Jogos/
  captura em segundo plano, hoje cai em `WIN_REGISTRY` genérico).
- Categorias novas para superfícies sem código hoje:
  - `BILL_CHECKOUT`, `BILL_WEBHOOK_VALIDATION`, `BILL_ENTITLEMENT_SYNC`
    (fundação de cobrança recorrente).
  - `APP_INV_SCAN`, `APP_INV_STARTUP_ENTRY` (aba Aplicativos: inventário de
    programas/entradas de inicialização).
  - `SEC_HEALTH_QUERY` (leitura de saúde do Windows Security Center na aba
    Sistema).
- `BugCodeClassifier` ganha métodos/branches de classificação para essas
  novas áreas, seguindo o padrão existente (contexto string + tipo de
  exceção).

## 3. `BugCode` por ação (ganho de causa-raiz)

- `WindowsActionJournalEntry` ganha `BugCode? BugCode { get; set; }`, ao lado
  de `OutcomeReason`.
- Nos pontos de `WindowsTransactionEngine` que hoje atribuem
  `item.Entry.OutcomeReason = ...` a partir de uma exceção capturada, também
  atribuir `item.Entry.BugCode =
  BugCodeClassifier.ClassifyOptimizationException(exception, entry.ActionId)`.
- `OptimizationReportLineDto` ganha `BugCode? BugCode { get; init; }`;
  `OptimizationReportBuilder.Build` propaga `entry.BugCode` para a linha.
- Broker (`Ralven.Broker`/cliente IPC no App), fluxo de auth e persistência de
  settings passam a chamar `BugCodeClassifier.ClassifyBrokerException`/
  `ClassifyException(ex, "auth")`/`ClassifyException(ex, "settings")` nos
  pontos onde hoje só logam/exibem a mensagem crua.

## 4. Nomes amigáveis (`BugCodeCatalog`)

- Novo `Ralven.Contracts.BugCodeCatalog`: `IReadOnlyDictionary<BugCode,
  string>` mapeando cada código para uma **chave de recurso** (não o texto
  em si), ex. `BugCode.BRK_ACTION_EXECUTION →
  "BugCode.BRK_ACTION_EXECUTION"`. Um teste garante que todo membro do enum
  tem entrada no catálogo e que a chave existe nos 3 `.resx`
  (`Strings.resx`, `.pt-BR`, `.es`) — fecha o contrato sem duplicar texto.
- Frases curtas e honestas, no padrão de mensagem de ação existente (ex.:
  "Falha ao executar a ação" para `BRK_ACTION_EXECUTION`), não uma descrição
  técnica do enum.
- Um `IBugCodeLocalizer`/método estático em `Ralven.App` faz `BugCode →
  string` usando o `ILocalizationService` já existente, com fallback para o
  nome cru do enum se a chave não existir (nunca quebra a UI).

## 5. UI do app

- Card de Resultado do Otimizador: quando `Succeeded == false`, o resumo
  geral e cada linha de ação com `Outcome` de falha/rollback-falhou mostram,
  em texto secundário (mesmo estilo dos metadados já exibidos na tela, sem
  novo componente visual): `"{mensagem amigável}. Código do erro: {código}"`
  — usa o `BugCode` mais específico disponível (por ação; se ausente, cai no
  código da run inteira).
- Texto copiável do relatório técnico e do bug report continuam mostrando o
  código, agora com o nome amigável na frente do código cru (retrocompatível
  — o código cru nunca desaparece do texto, só ganha contexto).
- Sem novo componente XAML: reaproveita o `TextBlock` secundário já usado
  para metadados de linha do relatório.

## 6. Worker (`infra/cloudflare-worker`)

- `bugCodes.js` ganha `BUG_CODE_LABELS` (código → rótulo curto pt-BR),
  mantido manualmente como o `ALLOWED_BUG_CODES` já é hoje — sem pipeline de
  geração de código.
- Novos códigos de `BugCode` entram em `ALLOWED_BUG_CODES` e
  `BUG_CODE_LABELS` no mesmo commit que os adiciona ao enum C#, para não
  ficar dessincronizado (teste de paridade novo, ver Testes).
- Nenhuma mudança de schema além da já existente `0008_bug_report_code.sql`.

## 7. Dashboard (`infra/dashboard`)

- Sem tocar o núcleo de `rendering.js`/`charts.js` (evita conflito com
  `feat/dashboard-insights`). Adiciona só:
  - `bugCodeLabel(code)` em `charts.js`, lendo de `bugCodeLabels.js` — um
    novo arquivo no dashboard que espelha manualmente `BUG_CODE_LABELS` do
    Worker, mesmo padrão de duplicação manual já usado por `bugCodes.js`/
    `ALLOWED_BUG_CODES` (dashboard e Worker são deploys independentes, sem
    import cross-package hoje).
  - `toBugReportRow` passa a incluir o nome amigável ao lado do código cru
    na mesma célula (ex.: `"BRK_ACTION_EXECUTION — Falha ao executar a
    ação"`), sem nova coluna nem novo layout.

## Testes

- `Ralven.Tests`: todo `BugCode` tem entrada em `BugCodeCatalog` e a chave de
  recurso existe nos 3 idiomas; `BugCodeClassifier` (já testado) ganha casos
  para as novas categorias e para classificação por `actionId`;
  `OptimizationReportBuilder` propaga `BugCode` por linha;
  `WindowsTransactionEngine`/journal testam que uma falha simulada grava
  `BugCode` coerente com a exceção.
- Worker: teste de paridade `ALLOWED_BUG_CODES`/`BUG_CODE_LABELS` cobrindo
  todo valor usado nos testes/fixtures; teste de `bug_code` presente em
  `recentFailures`/linha da tabela de bugs (se aplicável ao teste existente).
- Dashboard: teste de `toBugReportRow`/`bugCodeLabel` com código conhecido e
  com código desconhecido (fallback para o código cru).

## Fora de escopo

- Implantar a migration `0008` em produção (operação de deploy separada).
- Reescrever `rendering.js`/`charts.js` ou integrar `feat/dashboard-insights`.
- Auditoria de *todo* catch block do repositório — o escopo cobre as
  superfícies de falha visíveis ao usuário (ação de otimização, broker,
  auth, settings) mais as 3 categorias novas listadas no item 2.
