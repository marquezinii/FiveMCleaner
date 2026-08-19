# Design System: FiveMCleaner

<!--
Adaptado do formato DESIGN.md (Google Labs) para um app WPF nativo: sem
frontmatter YAML nem sidecar .impeccable/design.json (ambos pertencem ao
painel/linter web da Stitch, sem equivalente aqui). Tokens reais vivem em
XAML — `src/FiveMCleaner.App/Themes/` — este arquivo documenta o sistema
construído, não o substitui.
-->

## Overview

**Direção: "Prancheta técnica"**

A interface é uma prancha de desenho técnico. Cada página é uma **folha** que
ocupa a área de conteúdo inteira: bloco de legenda no topo, vista principal e
coluna de notas. A separação entre regiões é feita por **traço**, nunca por
cartão flutuando sobre cartão, e o fundo da folha carrega uma retícula de
papel milimetrado quase imperceptível — é o que impede uma área sem dado de
ler como vazio morto.

A metáfora não é decorativa: o produto revisa um **plano** antes de executá-lo,
registra **revisões** datadas e permite voltar ao estado anterior. Prancha,
quadro de ações e tabela de revisões são exatamente essas três coisas.

Este redesign **substituiu** a direção anterior ("Bancada de tuning premium":
grafite quase preto, laranja de instrumento, metal escovado). Aquele visual foi
tratado como anti-referência: nenhum de seus materiais sobreviveu.

**Key Characteristics:**
- Grafite azulado frio como base — nunca preto puro, que cansa a vista e apaga as hairlines
- Um único acento saturado: o ciano de tinta técnica, reservado a interação
- Estratégia de cor "Restrained": neutros cobrem quase toda a superfície
- Raio de canto quase reto (2–8px); `RadiusPill` só existe no interruptor e no avatar
- Sem geometria 3D, sem glow, sem gauge decorativo: profundidade vem de camada, traço e material
- Nenhuma página tem largura máxima travada — a folha é a janela

## Colors

### Primary
- **Tinta técnica** (`AccentBrush` #3E9FBA escuro / #17708A claro): CTA primário, foco, seleção, estado ativo, progresso e filete da vista principal. Nunca preenche superfície grande.
- `AccentTextBrush` para o acento aplicado a texto; `AccentBrightBrush` em hover; `AccentDeepBrush` em press.

### Brand
- **Laranja da marca** (`BrandInkBrush` #FF7A18 escuro / #D95E00 claro): vem do logotipo e continua sendo compromisso de marca. **Só pode aparecer no logotipo da barra de título.** Saiu da interface porque laranja saturado sobre fundo escuro lê como alerta permanente — era a fonte real do cansaço visual relatado no visual anterior. Um teste (`ThemeTokenContractTests.BrandInk_StaysOutOfTheInterfacePalette`) trava essa restrição.

### Neutral
- **Mesa** (`CanvasBaseBrush`): fundo da janela, fora da folha.
- **Poço** (`CanvasSunkenBrush`): campos de texto, trilhos, cabeçalho de tabela, coluna de notas.
- **Folha e degraus** (`Surface1Brush` → `Surface3Brush`): a folha, campos sobre ela, e superfícies flutuantes.
- **Texto** (`TextPrimaryBrush`/`TextSecondaryBrush`/`TextTertiaryBrush`): nunca um cinza fora dessas três chaves.
- **Traço** (`BorderSubtleBrush` → `BorderStrongBrush`): pesos de linha na lógica de desenho técnico — fina divide, média contorna, grossa delimita o operável.
- **Retícula** (`SheetGridBrush`, `GridLineBrush`, `TickMarkBrush`): papel milimetrado e marcações de margem.

### Semantic
`SuccessBaseBrush`, `WarningBaseBrush`, `DangerBaseBrush`, `InfoBaseBrush` e `RevertBaseBrush`, cada uma com par `*SurfaceBrush`/`*BorderBrush` quando aplicável. `InfoBaseBrush` é azul-aço, deliberadamente mais escuro e menos ciano que o acento, para os dois nunca se confundirem no gráfico ao vivo.

### Named Rules
**The One Ink Rule.** Ciano é a única cor saturada da interface e significa interação. Um dado que não é acionável nem é leitura de instrumento não recebe acento.

**The Brand-Stays-On-The-Logo Rule.** Laranja é marca, não interface. Qualquer uso de `BrandInkBrush` fora de `MainWindow.xaml` é regressão e falha em teste.

**The Contrast Floor.** Todo par (texto, fundo) realmente composto pela interface tem contraste ≥ 4.5:1 nos dois temas, verificado por teste — as escalas pequenas do app (Overline/Caption, 11–12px) não se qualificam como "texto grande".

## Typography

**Display/Body:** Segoe UI Variable (Display/Text) — face nativa do Windows 11.
**Readout:** Cascadia Mono (fallback Consolas). Ambas acompanham o Windows; nenhuma dependência nova.

### Hierarchy
- **Display** (34/40, SemiBold): recomendação do diagnóstico.
- **PageTitle** (24/30): título de cada folha, dentro do bloco de legenda.
- **Section/Subsection** (16/22, 14/20, SemiBold): cabeçalhos de bloco.
- **Body/BodyStrong** (14/20): texto corrido e nome de item.
- **Secondary/Caption** (12/17, 11/15): apoio e metadado.
- **Overline** (11/14, SemiBold, `TextTertiaryBrush`): rótulo de campo e **cabeçalho de coluna de tabela**.
- **Metric** (30/34, 20/24, display): um VALOR EM PALAVRAS ("Moderada", "Impacto moderado").
- **Readout** (28/34, 19/24, mono tabular): uma LEITURA NUMÉRICA ("88", "8,0 GB", "62%").
- **TickLabel** (mono, tabular, tertiary): graduação de escala, timestamp, marcação de margem.

### Named Rules
**The Number-vs-Word Rule.** `Readout*`/`TickLabel` só para o que é literalmente número lido de um instrumento. Aplicar mono a uma frase quebra a linha e lê como bug.

## Layout

**A folha é a janela.** Cada página é um `SheetSurface` esticado, com:

1. **Bloco de legenda** (`TitleBlockSurface`, largura total): título, contexto e a ação primária da página.
2. **Corpo**, dividido em faixas verticais que somam a largura inteira.

| Página | Faixas |
|---|---|
| Visão geral | vista principal (`*`) · notas (360) |
| Otimizador | controle (440) · plano (`*`) · registro (420, colapsa quando não há execução) |
| Histórico | tabela de revisões (`*`) · notas (340) |
| Configurações | categorias (200) · formulário (`*`, conteúdo 880 centralizado) · notas (400) |

Nenhuma página usa `MaxWidth` no nível da página. Regiões cujo conteúdo cresce
com o tempo (gráfico ao vivo, tabelas) ficam em linha `*` e consomem a altura
restante, então a folha termina exatamente na borda da janela.

`MainWindow` mantém mínimo 960×580 e abre maximizada.

## Elevation & Depth

Camadas tonais fazem quase todo o trabalho. Sombra (`Elevation2Shadow`,
`Elevation3Shadow`) só aparece em superfície que realmente flutua — popup de
combobox, `FloatingSurface`. Conteúdo em repouso na folha **nunca** tem sombra.

### Named Rules
**The Rule-Not-Card Rule.** Dentro da folha, separação é traço. Cartão dentro de
cartão foi o defeito estrutural do visual anterior e não deve voltar.

## Shapes

`RadiusXs 2`, `RadiusSm 3`, `RadiusMd 4`, `RadiusLg 6`, `RadiusXl 8`, `RadiusPill 999`.

`RadiusPill` é permitido **apenas** no trilho/thumb do interruptor e no avatar
circular. Card, campo, botão, trilho de progresso e etiqueta usam a escala
contida.

## Components

### Buttons
- **Primary** (`PrimaryButtonStyle`): altura 36, `RadiusMd`, preenchido com `AccentBrush`, texto `TextOnAccentBrush`; press aplica `scale 0.98`.
- **Secondary** (`SecondaryButtonStyle`): fantasma com borda `BorderDefaultBrush`, hover preenche `Surface2Brush`.
- **Danger ghost / Link / Icon**: variantes do fantasma.

### Toggle
`ToggleSwitchStyle` segue o padrão Fluent do Windows 11: trilho 40×20 vazado com
borda quando desligado, preenchido com o acento quando ligado, thumb de 12px que
**desliza** (`MotionControl` + `EaseControl`) e cresce para 14px em hover.
Substituiu a cápsula 42×24 com bolinha de 18px do visual anterior.

### Tables
`TableHeaderRowStyle` + `TableHeaderCellText` + `TableRowStyle`. Cabeçalho e
linha declaram **as mesmas larguras de coluna**, então alinham sem
`SharedSizeScope`. Sem listra zebrada, sem cartão por linha, sem barra lateral
colorida: risco e estado se leem por cor de texto + forma de ícone.

### Instruments
- **ProgressRailStyle**: trilho linear, `RadiusXs`, sem easing — reflete o dado real.
- **Escala graduada**: readout tabular grande + trilho + marcações 0/25/50/75/100. É o medidor de prontidão; substituiu o anel `ArcProgress` sobre o núcleo 3D `CoreVisual`, ambos **removidos do produto**.
- **LivePerformanceChart**: gráfico 2D leve, dentro de um poço com retícula, em linha `*`.

### Surfaces
`SheetSurface` (a folha) · `TitleBlockSurface` (bloco de legenda) · `HeroSurface` + `HeroAccentRule` (vista principal, marcada por filete de acento à esquerda) · `FieldSurface`/`PanelSurface` (região delimitada) · `InsetSurface` (poço) · `NotesColumnSurface` (coluna de notas) · `FloatingSurface` (popup).

### Navigation
Rail esquerdo (`ui:NavigationView`, WPF-UI nativo) sobre `SurfaceRailBrush`. O
chrome da janela usa acento neutro (`#232B37` escuro / `#CBD1DB` claro): o ciano
fica reservado ao conteúdo.

## Motion

Durações e curvas vivem em `Themes/Tokens/Motion.xaml` e nenhuma Storyboard nova
deve usar valor fora dali. `App.ApplyMotionPolicyToDurationTokens` zera essas
durações na inicialização quando o Windows pede menos animação — Storyboards
declaradas dentro de `ControlTemplate` são congeladas e não conseguem consultar
`MotionPolicy` em tempo de execução, então a política é aplicada na fonte.

## Do's and Don'ts

### Do:
- **Do** deixar a folha ocupar a janela inteira e dar linha `*` ao que cresce com o tempo.
- **Do** separar regiões por traço e cabeçalho, não por cartão aninhado.
- **Do** usar `Grid` com coluna `*` quando um `TextBlock` precisa quebrar ao lado de um ícone — `StackPanel Orientation="Horizontal"` dá largura infinita ao filho e `TextWrapping` nunca dispara.
- **Do** declarar as mesmas larguras no cabeçalho e na linha de uma tabela.
- **Do** referenciar sempre um recurso de `Themes/`, nunca um hex ou `CornerRadius` literal.

### Don't:
- **Don't** usar laranja em qualquer lugar da interface — é marca, e há teste travando isso.
- **Don't** aplicar `Readout*`/`TickLabel` a uma frase.
- **Don't** usar `RadiusPill` fora do interruptor e do avatar.
- **Don't** reintroduzir geometria 3D, anel decorativo, glow ou gauge sem dado real por trás.
- **Don't** travar a largura de uma página com `MaxWidth` no nível da página: era exatamente o que deixava metade da janela vazia.
- **Don't** preencher espaço com conteúdo inventado. Uma coluna de notas só existe quando carrega informação real do produto.
