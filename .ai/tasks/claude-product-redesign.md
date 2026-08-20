# Redesign completo de produto (design system, shell e todas as páginas)

- **Agente**: claude
- **Branch**: `ai/claude/product-redesign` (base: `dev/proxima-versao`)
- **Worktree**: `C:\Projetos\FiveMCleaner-product-redesign`
- **Status**: pronto para integração

## Objetivo

Redesenho visual e de UX completo do FiveMCleaner, não uma "maquiagem"
(sem trocar cor/raio/gradiente por cima da estrutura antiga): criar um
sistema de design real (tokens de cor/tipografia/espaçamento/raio/motion),
reconstruir shell, navegação e as quatro páginas principais (Visão geral,
Otimizador, Histórico, Configurações) e unificar as janelas secundárias sob
a mesma linguagem visual — preservando a marca, laranja como acento (nunca
preenchimento grande), todos os dados reais (nenhuma métrica de FPS/score
inventada), os invariantes de segurança (`docs/safety.md`) e a arquitetura
de camadas (`docs/architecture.md`), incluindo o bloqueio permanente do
GTA V Enhanced.

O processo seguiu três fases: (1) direção de produto/design com um subagente
Opus 5, (2) implementação com Sonnet 5 em ciclos de
build → captura de tela → inspeção visual → correção, (3) uma segunda
revisão crítica e independente do Opus 5 sobre o resultado construído,
com uma lista de 15 problemas priorizados corrigidos nesta mesma tarefa.

## Conceito visual

Um "console de diagnóstico" sóbrio: superfícies quase pretas com uma escala
de cinza-azulado neutra, tipografia como hierarquia principal (não cor),
laranja reservado a foco de atenção pontual (CTA primário, estado ativo,
métrica em destaque, ring de progresso) — nunca como fundo de página ou
superfície grande. O elemento de assinatura é um núcleo 3D poligonal
(`CoreVisual`) que reage a dados reais (prontidão local ou progresso de
execução), não decorativo.

## O que mudou

### Sistema de design (`Themes/`)

- **Tokens** (`Themes/Tokens/Colors.Dark.xaml`, `Colors.Light.xaml`,
  `Radii.xaml`, `Motion.xaml`, novos): paleta semântica completa
  (`Surface1/2/3`, `TextPrimary/Secondary/Tertiary`, `AccentBase/Bright/Deep`,
  `Success/Warning/Danger/Info/Revert`), raios e durações/easings nomeados.
  Trocados como dicionário inteiro por tema (não por brush individual) em
  `Services/ThemeManager.cs`.
- **Tipografia** (`Themes/Typography.xaml`, novo): escala de 12 estilos
  nomeados (`DisplayText` → `CaptionText`, `MetricText`, `MonoText`), fonte
  variável Segoe UI declarada uma única vez.
- **Superfícies** (`Themes/Surfaces.xaml`, novo): vocabulário único de
  contêiner (`PanelSurface`, `FloatingSurface`, `InsetSurface`, `HeroSurface`,
  `DialogSurface`, `ListRowSurface`, divisores horizontais/verticais) para
  eliminar a repetição de "cartão dentro de cartão".
- **Ícones** (`Themes/Icons.xaml`): conjunto vetorial ampliado (navegação,
  selos de resultado, alerta, cópia, link externo) com estilos `Icon16/20/24`.
- **Controles** (`Themes/Controls.xaml`): reescrito — botões primário
  (único lugar com `ScaleTransform`, banido de listas por teste de
  contrato), secundário, ghost de perigo, link com estados de hover/foco;
  campos de formulário, toggle, segmentado, `ProgressRailStyle` genérico,
  linhas de navegação e de configurações, linhas de ledger/alerta.

### Elemento 3D de assinatura

`HoloCore3D.cs` e `OptimizerCore3D.cs` (quase duplicados) foram fundidos em
um único `Controls/CoreVisual.cs` com `Mode="Readiness"` (Visão geral) e
`Mode="Engine"` (Otimizador, com anéis extras). Paleta separada em
`CoreVisualPalette.cs` porque materiais 3D não aceitam `DynamicResource`.

### Shell (`MainWindow.xaml`/`.xaml.cs`)

Drasticamente reduzido: barra de título com marca, badge de edição, cluster
de status global (substituindo banners espalhados pelas páginas), Discord,
divisor vertical e botão de conta (avatar/iniciais reais quando logado,
rótulo "Entrar / Cadastre-se" localizado e escondido quando logado — bug
real corrigido nesta tarefa, ver seção de revisão). Navegação lateral com
`ui:NavigationView` hospedando três páginas como `UserControl` próprios e
Configurações inline (mantida em `MainWindow.xaml` por acoplamento real com
`MainWindow.Account.xaml.cs`).

### Páginas novas (`Views/Pages/`)

- **`OverviewPage`**: detecção FiveM/GTA V, hero de recomendação com
  `CoreVisual` + `ArcProgress`, número de prontidão como bloco de texto
  próprio (não mais sobreposto ao 3D), última otimização e faixa de KPIs
  sem superfície de cartão (só divisores), bloco "Desempenho ao vivo" com
  pílula de estado real (Ao vivo / Lendo… / Indisponível) e gráfico que vira
  um placeholder de espera em vez de renderizar vazio, definição do
  computador local, prontidão para streaming.
- **`OptimizerPage`**: `SpectrumSelector` (novo controle) substitui o antigo
  "hero + três cartões" por um único trilho com três paradas; hero com nome
  do perfil em `PageTitleText` e selo "Recomendado" como marca separada
  (não mais concatenado em texto maiúsculo); indicadores; plano de ação
  agrupado por categoria com barra de risco semântica e marca de UAC; três
  blocos de fase (preparar/executar/resultado) com `CoreVisual` reagindo a
  intensidade real; comparação antes/depois; relatório técnico exportável.
- **`HistoryPage`**: timeline/ledger (trilho + nó por entrada) em vez de
  cartão-de-cartões; estado vazio real (sem item fantasma).
- **Configurações** (inline em `MainWindow.xaml`): layout de duas regiões
  categorias | conteúdo, com divisor vertical, categoria padrão "Geral"
  (antes "Conta", que ficava vazia para quem não está logado).

### Janelas secundárias

`AccountWindow`, `BugReportWindow`, `PrivacyConsentWindow`,
`OptimizationConfirmationWindow`, `TermsOfUseWindow`: migradas para o novo
vocabulário de tokens/superfícies/controles (não redesenhadas visualmente
peça a peça — ver limitações).

### ViewModel e serviços

`MainViewModel.cs` mantido como está (~2700 linhas, decisão de escopo
deliberada — dividi-lo teria custo/risco desproporcional ao pedido).
Adições pontuais: propriedades computadas de resultado
(`ReportSucceeded`/`ReportHasIsolatedFailures`/`ReportFailedOutright`/
`ReportHasRollbackFailures`), estado real de "Desempenho ao vivo"
(`IsLivePerformanceLive/Waiting/Unavailable`, `HasLiveMetricsSample`),
`SelectedProfileName`/`IsSelectedProfileRecommended` separados de
`SelectedProfileLabel`. Novos: `Services/ExternalLauncher.cs` (link externo
centralizado), `Services/MotionPolicy.cs` (respeita animação reduzida do
Windows).

## Revisão crítica do Opus e correções (15 itens)

Um segundo agente Opus 5, sem ter implementado nada, revisou o resultado
construído (screenshots reais + código) de forma independente e apontou 15
problemas priorizados. Todos os 15 foram corrigidos nesta mesma tarefa:

1. Colapso de largura (`HorizontalAlignment="Left"` competindo com
   `MaxWidth` deixava o conteúdo com metade da largura da janela) — trocado
   por `Stretch` em `OverviewPage`, `OptimizerPage`, `HistoryPage` e
   Configurações.
2. Subtítulo do Otimizador flutuando fora do alinhamento do cabeçalho.
3. Configurações praticamente vazia por padrão: novo estilo
   `SettingsCategoryItemStyle` (barra de acento, sem preenchimento total),
   categoria padrão trocada de "Conta" para "Geral", divisor vertical entre
   categorias e conteúdo.
4. Item fantasma no Histórico vazio removido do ViewModel; o estado vazio
   real (já existente na página) passou a aparecer de fato.
5. Número de prontidão sobreposto ao núcleo 3D: separado em bloco de texto
   próprio abaixo do núcleo (que encolheu de 180px para caber dentro do
   anel de progresso sem se misturar ao número).
6. Cor do núcleo 3D marrom/saturada demais no tema escuro: `fillLight`
   trocado de `#5A3814` para `#2A3038` (cinza-azulado frio); coluna do
   núcleo no Otimizador aumentada (200→240px de coluna, 180→220px de núcleo).
7. Bloco "Desempenho ao vivo" contraditório (pílula "AO VIVO" fixa enquanto
   os valores liam "Lendo…" ou "Indisponível"): pílula agora reflete um
   estado real (`IsLivePerformanceLive/Waiting/Unavailable`) com cor, ponto
   e texto coerentes; o gráfico vira um placeholder textual de espera em vez
   de desenhar eixos vazios.
8. Linguagem de contêiner inconsistente na Visão geral: removida a
   superfície de cartão da última otimização e da faixa de KPIs, que agora
   fluem como o resto da página, separadas por divisores horizontais.
9. Caixa alta inconsistente em rótulos `OverlineText`: 9 chaves de
   localização (`Optimizer.Impact`, `Optimizer.Execution`,
   `Privacy.Collects/DoesNotCollect.Title`, `Plan.VerifiedActions.Label`,
   `Dashboard.LastRun.Title`, `Optimizer.Stage.Prepare/Run/Result`)
   normalizadas para maiúsculas nos três idiomas (en/pt-BR/es), alinhando
   com a maioria já maiúscula da mesma família tipográfica.
10. Lista do plano de otimização melhorada: agrupamento real por categoria
    (`CollectionViewSource` com `PropertyGroupDescription`), barra de risco
    lateral com cor semântica (`ActionDisplayItem` ganhou `RiskBrushKey`,
    `RequiresElevation`, `CategoryLabel`), marca de escudo (UAC) ao lado do
    rótulo de privilégio quando a ação exige administrador.
11. Selos de detecção FiveM/GTA V duplicados no Otimizador removidos —
    existem só na Visão geral agora (teste de contrato atualizado para
    refletir a decisão).
12. Tipografia do hero do Otimizador: nome do perfil em `PageTitleText`
    (24px, frase normal) com o selo "Recomendado" como marca separada
    (tique + rótulo, no mesmo padrão já usado no `SpectrumSelector`), não
    mais concatenado em uma única string maiúscula de 34px.
13. `SpectrumSelector` ganhou affordances reais: três marcas fixas (ticks)
    ao longo do trilho, polegar maior (12→18px) com anel de foco visível,
    trilho inteiro agora clicável (seleciona a parada mais próxima) e
    navegável por teclado (setas esquerda/direita), realce de cor no hover.
14. Botão "Revisar plano" morto removido (só rolava até uma seção já
    visível); handler e âncora órfã removidos do code-behind.
15. Cluster de conta na barra de título: divisor vertical entre Discord e
    Conta; textos antes fixos em português (`ToolTip`, `AutomationProperties.Name`,
    rótulo "Entrar / Cadastre-se") migrados para `LocalizedStrings`; bug real
    corrigido — o rótulo "Entrar / Cadastre-se" nunca era escondido ao logar,
    então um usuário autenticado via o avatar/iniciais corretos ao lado do
    texto de convite para login, simultaneamente. Um botão de bug report na
    barra de título foi cogitado e revertido: um teste de contrato já trava
    deliberadamente que relatar bug e copyright vivem só em Configurações,
    não no shell global — respeitei essa decisão já tomada e testada em vez
    de duplicá-la.

## Achado incidental corrigido durante a revisão (fora da lista do Opus)

Ao investigar um item da lista, encontrei um bug de correção genuína e
independente: em `OptimizerPage.xaml`, o selo de detecção FiveM/GTA V usava
um ícone fixo sempre verde (`IconCheck`), ignorando `IsFiveMLegacyDetected`/
`IsGtaVLegacyDetected`, ao contrário da versão da Visão geral (que já reagia
via `DataTrigger`). Corrigido para o mesmo padrão — depois removido de novo
no item 11 da lista Opus, quando o selo do Otimizador foi eliminado por
duplicidade.

## Concorrência com outra tarefa no mesmo diretório

Durante a validação final, encontrei `AccountWindow.xaml`/`.xaml.cs` num
estado que não compilava (chamadas a `T(...)`/`F(...)` sem essas funções
existirem) — trabalho de localização iniciado antes desta sessão e deixado
incompleto. Reverti para o último commit para restaurar um build
funcionando; o usuário então iniciou, numa sessão separada, a tarefa
sugerida "Localize hardcoded pt-BR strings in AccountWindow.xaml"
(`task_e75cfebf`), que terminou de migrar `AccountWindow` (janela de
login/cadastro) para `T()`/`F()` com chaves de localização completas nos
três idiomas. Essa mudança está presente nesta branch e nesta tarefa apenas
a herdou e validou — não a re-implementei nem a revertive depois de pronta.

## Testes e validação

- `dotnet build FiveMCleaner.slnx`: sem avisos, sem erros.
- `dotnet test FiveMCleaner.slnx`: **775/775 aprovados** (Debug e Release).
- `dotnet format FiveMCleaner.slnx --verify-no-changes`: aprovado.
- `scripts/Verify-Safety.ps1`: aprovado (build Release + suíte completa).
- `scripts/Install-DevelopmentShortcut.ps1 -Build`: atalho
  "FiveMCleaner - Desenvolvimento" reconstruído com sucesso.
- Inspeção visual real via `--demo --demo-synthetic --capture=<path>
  --capture-page=<Overview|Optimizer|History|Settings>` do próprio binário
  Release, nas quatro páginas, após a rodada de correções do Opus —
  confirmando visualmente: número de prontidão separado do núcleo 3D,
  divisor no cluster de conta, pílula de estado real em "Desempenho ao
  vivo", agrupamento por categoria no plano, Configurações abrindo em
  "Geral" com conteúdo visível, Histórico com estado vazio real.
- Testes de contrato de UI (`LocalizedInterfaceContractTests`,
  `AccountWindowTests`, `OptimizationInterruptionUiTests`) reescritos onde a
  mudança de marcação era intencional, com comentário explicando a decisão.

## Limitações e observações conhecidas

- As cinco janelas secundárias (`AccountWindow`, `BugReportWindow`,
  `PrivacyConsentWindow`, `OptimizationConfirmationWindow`,
  `TermsOfUseWindow`) foram migradas para os novos tokens/estilos, mas não
  redesenhadas visualmente peça a peça como as quatro páginas principais —
  funcionais e coerentes com o novo vocabulário, mas não passaram pela
  mesma inspeção Opus.
- A seção "Conta" de Configurações (`MainWindow.xaml`, cartões de alterar
  senha/e-mail/excluir conta) ainda contém alguns literais fixos em
  português (ex.: "Alterar senha", "Excluir conta", "Entrar / Cadastre-se"
  no botão de CTA de Configurações) não cobertos por esta tarefa nem pela
  migração de `AccountWindow` — candidato a uma tarefa de localização
  dedicada.
- `Controls/VectorPathIcon.cs` foi criado como alternativa a
  `ui:SymbolIcon` para ícones vetoriais customizados na navegação, mas a
  navegação final usa `ui:SymbolIcon` por estabilidade; o arquivo ficou no
  repositório, funcional e testável, mas sem uso atual — candidato a
  remoção futura se permanecer não referenciado.
- O núcleo 3D (`CoreVisual`) ainda tem um tom levemente quente nas facetas
  visíveis mesmo após a correção da luz de preenchimento (item 6): vem do
  reflexo especular intencionalmente laranja (acento da marca), não da luz
  de preenchimento que motivou o achado do Opus — mantido como decisão de
  design, não como bug remanescente.
- Sem mudança de versão, release, instalador, updater ou deploy.

## Commits

Um único commit local nesta tarefa, com todas as mudanças descritas acima
(sistema de design, shell, quatro páginas, janelas secundárias, e as 15
correções da revisão Opus). Mensagem e hash: ver `git log` desta branch.
