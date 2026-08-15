# Design System: FiveMCleaner

<!--
Adaptado do formato DESIGN.md (Google Labs) para um app WPF nativo: sem
frontmatter YAML nem sidecar .impeccable/design.json (ambos pertencem ao
painel/linter web da Stitch, sem equivalente aqui). Tokens reais vivem em
XAML — `src/FiveMCleaner.App/Themes/` — este arquivo documenta o sistema
construído, não o substitui.
-->

## Overview

**Direção: "Bancada de tuning premium"**

FiveMCleaner é uma ferramenta de diagnóstico e tuning de PC para FiveM/GTA V
Legacy — não um dashboard SaaS genérico nem um software de periférico gamer.
A interface lê como uma bancada de instrumentação de precisão: grafite quase
preto, laranja como luz de instrumento (não decoração), metal escovado como
segundo material reservado a momentos de destaque (o bezel dos medidores, a
borda do painel-herói). O logo do produto já usa exatamente esses dois
materiais — laranja + metal sobre grafite — então a interface não inventa uma
identidade nova, ela a estende para cada superfície.

A arquitetura de tokens (ResourceDictionary com troca de dicionário inteiro
por tema, nunca chave a chave) já era sólida antes deste redesign e foi
preservada; o que mudou são os valores dentro dela e a composição de cada
página. Contido, não gritado: sem gauges decorativos, sem glow em excesso,
sem clichê de "RGB gamer".

**Key Characteristics:**
- Grafite quase preto como base; laranja como único acento de ignição (estratégia "Committed", não "Drenched")
- Metal escovado (`SteelBrush`/`MetalEdgeBrush`/`InstrumentBezelBrush`) reservado ao painel-herói e ao bezel dos medidores — nunca espalhado
- Leituras numéricas em monoespaçado tabular; frases e rótulos em fonte de display proporcional — nunca os dois papéis na mesma face
- Raio de canto contido ("usinado", não "bolha de vidro")
- Sem barra colorida lateral decorativa em linhas de lista; risco/estado se lê por cor de texto + forma de ícone

## Colors

Paleta "Committed": neutros de grafite/metal cobrindo quase toda a superfície, laranja como único acento saturado, reservado a CTA, foco, estado ativo e leitura de instrumento.

### Primary
- **Laranja de ignição** (`AccentBrush` #FF7A18 escuro / #C85300 claro): CTA primário, foco, seleção ativa, progresso, glow não-temável do `CoreVisual` (`Controls/CoreVisual.cs`, hardcoded pois materiais 3D não aceitam `DynamicResource`). Nunca fundo de superfície grande.

### Secondary
- **Metal escovado** (`SteelBrush`/`MetalEdgeBrush`/`InstrumentBezelBrush`): segundo material da marca, não um cinza a mais. Usado só em: borda do `HeroSurface` (painel-herói de cada página) e no `TrackBrush` do `ArcProgress` (bezel do medidor de prontidão/otimizador). Se um componente novo quiser "parecer premium", este é o recurso — não mais laranja.

### Neutral
- **Canvas** (`CanvasBaseBrush`/`CanvasSunkenBrush`): fundo da janela e poços (campos de texto, trilhos de progresso).
- **Surface 1/2/3** (`Surface1Brush`→`Surface3Brush`): degraus de elevação, do painel mais raso ao mais alto (popover/dropdown).
- **Texto** (`TextPrimaryBrush`/`TextSecondaryBrush`/`TextTertiaryBrush`): hierarquia de leitura; nunca cinza fora dessas três chaves.
- **Bordas** (`BorderSubtleBrush`→`BorderStrongBrush`): hairlines e contornos de campo.

Semânticas (`SuccessBaseBrush`, `WarningBaseBrush`, `DangerBaseBrush`, `InfoBaseBrush`, cada uma com par `*SurfaceBrush`/`*BorderBrush`) inalteradas neste redesign — já eram corretas e não competiam com o laranja.

### Named Rules
**The One Accent Rule.** Laranja aparece em CTA, foco, ativo e leitura de progresso — nunca como preenchimento de superfície além do botão primário e do wash do painel-herói (`SurfaceAccentWashBrush`, uma tinta âmbar quase imperceptível, não um fundo laranja).

**The Metal-Is-Earned Rule.** `MetalEdgeBrush`/`InstrumentBezelBrush` só aparecem no painel-herói e no bezel dos medidores de cada página — no máximo um por tela. Um terceiro uso na mesma tela é ruído, não reforço de marca.

## Typography

**Display/Body Font:** Segoe UI Variable (Display/Text) — face nativa do Windows 11, correta para um app "Operate"; nenhuma face custom foi introduzida.
**Readout Font:** Cascadia Mono (fallback Consolas) — ambas já acompanham o Windows, nenhuma dependência nova.

**Character:** Segoe UI Variable carrega toda frase, rótulo e título — legível, neutro, correto para uma ferramenta técnica. Cascadia Mono é reservado exclusivamente a leituras numéricas ao vivo (a diferença é deliberada: ver Named Rule abaixo).

### Hierarchy
- **Display** (`DisplayText`, SemiBold, 34/40): recomendação do diagnóstico na Visão Geral.
- **PageTitle** (`PageTitleText`, SemiBold, 24/30): título de cada página.
- **Section/Subsection** (`SectionText` 16/22, `SubsectionText` 14/20, SemiBold): cabeçalhos de bloco dentro de uma página.
- **Body/BodyStrong** (`BodyText`/`BodyStrongText`, 14/20): texto corrido e nome de item de lista.
- **Secondary/Caption** (`SecondaryText` 12/17, `CaptionText` 11/15): descrição de apoio e metadado.
- **Overline** (`OverlineText`, SemiBold, 11/14, `TextTertiaryBrush`): rótulo de campo/instrumento acima de um valor — nunca kicker decorativo acima de um título.
- **Metric** (`MetricText` 30/34, `MetricSmallText` 20/24, `AppFontDisplay`): um VALOR EM TEXTO em destaque ("Moderada", "UAC somente ao executar").
- **Readout** (`ReadoutText` 28/34, `ReadoutSmallText` 19/24, `AppFontMono`, tabular): uma LEITURA NUMÉRICA ao vivo ("88", "43", "8,0 GB", "62%").

### Named Rules
**The Number-vs-Word Rule.** `Readout*` é só para o que é literalmente um número lido de um instrumento (score, contagem, percentual, GB). `Metric*` é para tudo que é um valor em palavras. Aplicar mono a uma frase quebra a linha e lê como bug, não como precisão técnica — este é o erro mais fácil de reintroduzir ao adicionar uma tela nova.

## Layout

Grade de página única (`StackPanel`/`Grid` com `MaxWidth` 1100–1240, margens 32/24), sem breakpoints — janela desktop redimensionável com mínimo definido por página (`MainWindow` 960×580). Densidade alta mas hierárquica: cada bloco separado por `HairlineDivider`/`VerticalHairlineDivider`, nunca por card aninhado. Uma página tem no máximo um painel-herói (`HeroSurface`/`PanelSurfaceElevated`); o resto é conteúdo direto no fluxo da página.

## Elevation & Depth

Sistema híbrido: camadas tonais (`Surface1`→`Surface3`) fazem a maior parte do trabalho de profundidade; sombra (`Elevation2Shadow`/`Elevation3Shadow`, `DropShadowEffect` suave com offset vertical) só aparece em superfícies elevadas/flutuantes (popover, painel elevado do Otimizador), nunca em conteúdo em repouso no fluxo da página.

### Shadow Vocabulary
- **Elevation2Shadow** (`Opacity 0.26/0.08` escuro/claro, `BlurRadius 24`, `ShadowDepth 6`): painéis elevados (`PanelSurfaceElevated`).
- **Elevation3Shadow** (`Opacity 0.40/0.14`, `BlurRadius 48`, `ShadowDepth 14`): superfícies flutuantes (popup de combobox, `FloatingSurface`).

### Named Rules
**The Bezel-Not-Shadow Rule.** O painel-herói e os medidores usam borda de metal (`MetalEdgeBrush`/`InstrumentBezelBrush`), não sombra, para se destacar — o efeito é de peça usinada encaixada, não de cartão flutuando.

## Shapes

Escala de raio contida e "usinada", não a bolha de app de consumo: `RadiusXs 3`, `RadiusSm 5`, `RadiusMd 6`, `RadiusLg 10`, `RadiusXl 13`, `RadiusPill 999` (só para toggle, thumb de slider e badges/pills reais). Trilhos de progresso lineares (`ProgressRailStyle`) usam `RadiusXs`, não pill — leem como régua de instrumento, não como barra de app.

## Components

### Buttons
- **Shape:** `RadiusMd` (6px).
- **Primary** (`PrimaryButtonStyle`): fundo `AccentBrush`, texto `TextOnAccentBrush`, sem borda; press dá `scale 0.98`.
- **Secondary** (`SecondaryButtonStyle`): fantasma com borda `BorderDefaultBrush`, hover preenche `Surface2Brush`.
- **Danger ghost / Link / Icon:** variantes do fantasma para ação destrutiva, ação terciária e botão de ícone (title bar).

### Progress & Instruments
- **ArcProgress** (anel): `TrackBrush="InstrumentBezelBrush"` (bezel de metal), `ProgressBrush="AccentGradientBrush"`. É o componente-assinatura do redesign — usado no medidor de prontidão (Visão Geral) e no "motor" do Otimizador.
- **ProgressRailStyle** (trilho linear): `RadiusXs`, fundo `CanvasSunkenBrush`, preenchimento na cor semântica do dado (CPU=laranja, GPU=azul info).
- **CoreVisual**: icosaedro 3D facetado, grafite difuso com emissivo laranja (`#FF7A18` hardcoded — ver nota em Colors). Não recebeu mudança de geometria/material neste redesign, só herda o novo bezel ao redor via `ArcProgress`.

### Cards / Panels
- **PanelSurface** (`RadiusLg`, borda `EdgeLightBrush`): superfície padrão.
- **HeroSurface** (`RadiusXl`, fundo `SurfaceAccentWashBrush`, borda `MetalEdgeBrush`): um por página, o único momento com bezel de metal.
- **InsetSurface**: poço para conteúdo denso (gráfico ao vivo).

### Ledger rows (listas)
- `LedgerRowStyle`: linha com hairline inferior, sem card aninhado, sem barra lateral colorida — risco/estado se lê pela cor do texto/ícone, nunca por uma faixa de 2–3px do lado.

### Inputs / Fields
- **Style** (`FormTextBoxStyle`): poço `CanvasSunkenBrush`, borda `BorderStrongBrush`, `RadiusMd`.
- **Focus:** borda vira `AccentBrush` (sem glow).
- **Error:** borda `DangerBaseBrush` via `Validation.HasError`.

### Navigation
- Rail esquerdo (`ui:NavigationView`, WPF-UI nativo) sobre `SurfaceRailBrush`; item selecionado usa o accent neutro do WPF-UI (não laranja) — o laranja fica reservado para dentro do conteúdo, não para o chrome de navegação.

## Do's and Don'ts

### Do:
- **Do** reservar `Readout*`/`AppFontMono` só a números lidos de um instrumento (score, %, GB, contagem).
- **Do** usar `MetalEdgeBrush`/`InstrumentBezelBrush` no máximo uma vez por página (painel-herói OU bezel de medidor).
- **Do** ler risco/estado por cor do texto + forma do ícone, nunca por barra lateral colorida em linha de lista.
- **Do** manter FluentWindow/Mica/TitleBar/NavigationView do WPF-UI para o chrome nativo da janela — não recriar chrome de janela do zero.
- **Do** referenciar sempre um `DynamicResource`/`StaticResource` de `Themes/`, nunca um hex ou `CornerRadius` literal numa página nova.

### Don't:
- **Don't** aplicar `Readout*` a uma frase ou palavra — quebra layout e lê como bug (era o defeito real encontrado e corrigido durante a verificação visual deste redesign).
- **Don't** adicionar um segundo painel-herói ou um segundo uso de metal escovado na mesma página — dilui o momento.
- **Don't** usar `RadiusPill` fora de toggle/thumb/badge — trilhos e cards usam a escala contida.
- **Don't** introduzir glow decorativo, gauge sem dado real por trás, ou paleta "gamer RGB" genérica — a marca é bancada de precisão, não periférico.
- **Don't** trocar o laranja por outra cor de acento: é compromisso de marca do logo, preservado em qualquer expansão futura do sistema.
