# Design System: FiveMCleaner

<!--
Adaptado do formato DESIGN.md (Google Labs) para um app WPF nativo: sem
frontmatter YAML nem sidecar .impeccable/design.json (ambos pertencem ao
painel/linter web da Stitch, sem equivalente aqui). Tokens reais vivem em
XAML — `src/FiveMCleaner.App/Themes/` — este arquivo documenta o sistema
construído, não o substitui.
-->

## Overview

**Direção: "Câmara Âmbar"**

A interface é uma sala escura com **uma lâmpada âmbar baixa**, ancorada no
canto inferior esquerdo. As únicas coisas acesas dentro dela são os números
reais da máquina do usuário.

A luz não é uma metáfora decorativa: ela é um `RadialGradientBrush` estático
(`AmbientLightBrush`) pintado uma vez atrás de todo o conteúdo da janela, e
está ancorada exatamente onde vivem o rail de navegação e a ação primária de
cada página. A luz cai sobre o que o usuário vai tocar.

Isso resolve um problema documentado do produto: o laranja da marca havia sido
banido da interface porque "laranja saturado sobre fundo escuro lê como alerta
permanente". Isso era verdade **enquanto ele aparecia em manchas chapadas sobre
um fundo que não o esperava**. Aqui a sala inteira já está sob essa luz, então
o acento pertence à cena em vez de gritar contra ela — e o compromisso de marca
volta a existir na interface, não só no logotipo.

Este redesign **substituiu** a direção anterior ("Prancheta técnica": folha de
papel emoldurada, retícula milimetrada, traço fino, cantos quase retos, acento
ciano). Aquele visual foi tratado como anti-referência: nenhum de seus
materiais sobreviveu. A direção anterior a ela ("Bancada de tuning premium")
também não.

**Key Characteristics:**
- Grafite **quente**, nunca azulado — um fundo frio sob luz âmbar vira cinza sujo
- Uma lâmpada: um radial âmbar, estático, no canto inferior esquerdo
- Estratégia de cor "Restrained": neutros cobrem a superfície, só o âmbar carrega interação
- Cantos de 4–18px — arredondados o bastante para a silhueta ler como outro app antes de qualquer cor aparecer
- Painéis se erguem por **sombra e tom**, não por borda grossa; nunca painel dentro de painel
- Uma única família tipográfica carrega tudo; mono só para número medido
- Sem geometria 3D, sem glow, sem medidor decorativo

## Colors

### Primary
- **Âmbar** (`AccentBrush` #E8720F escuro / #C3540A claro): CTA primário, foco, seleção, estado ativo, progresso, indicador do rail e a própria luz do ambiente.
- `AccentTextBrush` para o acento aplicado a texto; `AccentBrightBrush` em hover; `AccentDeepBrush` em press; `AccentSoftBrush`/`AccentWashBrush` para preenchimentos suaves.

### Brand
- `BrandInkBrush` (#FF7A18 escuro / #C4520A claro) é o laranja puro do logotipo, usado no wordmark da barra de título. O resto da interface usa a família `Accent*`, que é a mesma cor calibrada para contraste sobre cada superfície.

### Neutral
- **Chão** (`CanvasBaseBrush`): o fundo da janela, sob a lâmpada.
- **Poço** (`CanvasSunkenBrush`): campos, trilhos, gráfico ao vivo.
- **Painel** (`PanelFillBrush`): gradiente sutil de Surface1, mais claro no alto e mais fundo embaixo. É o que dá material a um painel momentaneamente vazio.
- **Degraus** (`Surface1Brush` → `Surface3Brush`) e **rail** (`SurfaceRailBrush`).
- **Texto** (`TextPrimaryBrush`/`TextSecondaryBrush`/`TextTertiaryBrush`): nunca um cinza fora dessas três chaves.
- **Traço** (`BorderSubtleBrush` → `BorderStrongBrush`): fina separa dentro de um bloco, média contorna um bloco, grossa delimita o operável.

### Semantic
`SuccessBaseBrush`, `WarningBaseBrush`, `DangerBaseBrush`, `InfoBaseBrush` e
`RevertBaseBrush`, cada uma com par `*SurfaceBrush`/`*BorderBrush` quando
aplicável. Cor semântica descreve **desfecho**, nunca decoração.

### Named Rules
**The One Light Rule.** Âmbar é a única cor saturada da interface e significa
interação ou a própria luz da sala. Um dado que não é acionável nem é leitura
de instrumento não recebe acento.

**The Contrast Floor.** Todo par (texto, fundo) realmente composto pela
interface tem contraste ≥ 4.5:1 nos dois temas, verificado por
`ThemeTokenContractTests` — as escalas pequenas do app (Overline/Caption,
12px) não se qualificam como "texto grande". Os extremos do gradiente de
painel também foram verificados; o mais apertado fica em 5.72:1.

**The Shape-Too Rule.** Todo estado lido por cor tem uma segunda pista de
forma: o selo de detecção troca check por X, a marca de desfecho troca de
glifo, e a estação corrente da espinha do Otimizador troca de anel vazado para
disco cheio.

## Typography

**Corpo, título, rótulo, botão e dado:** Segoe UI Variable (Text/Display).
Uma família só é deliberado — o pareamento display/corpo pertence a superfícies
de marca e aqui produziria ruído, porque esta tela tem muito mais elementos de
texto que uma landing page.
**Readout:** Cascadia Mono (fallback Consolas). Ambas acompanham o Windows.

### Hierarchy
- **Display** (32/38, SemiBold): a recomendação do diagnóstico. Uma por página, no máximo.
- **PageTitle** (22/28): título de cada página.
- **Section/Subsection** (17/24, 15/21, SemiBold): cabeçalhos de bloco.
- **Body/BodyStrong** (14/21): texto corrido e nome de item.
- **Secondary** (13/19) e **Caption** (12/17): apoio e metadado.
- **Overline** (12/16, SemiBold, terciário): rótulo de campo e cabeçalho de coluna.
- **Metric** (28/34, 20/26, display): um VALOR EM PALAVRAS ("Moderada").
- **Readout** (26/32, 18/24, mono tabular): uma LEITURA NUMÉRICA ("88", "8,0 GB").
- **TickLabel** (mono, tabular, terciário): graduação de escala e carimbo de data.

Cada degrau é ~1.15–1.2x do anterior. A escala anterior tinha cinco degraus
espremidos entre 11 e 14px, então metade da hierarquia era invisível.

### Named Rules
**The Number-vs-Word Rule.** `Readout*`/`TickLabel` só para o que é literalmente
um número lido de um instrumento. Mono numa frase quebra o ritmo da linha sem
medir nada — "Última varredura às 21:48" é `CaptionText`, não `TickLabel`.

## Layout

**A janela não emoldura a página.** Cada página é: cabeçalho direto sobre o
chão da câmara (sem faixa, sem borda), seguido de painéis irmãos que se erguem
desse chão.

| Página | Composição |
|---|---|
| Visão geral | diagnóstico + ao vivo (`*`) · notas (340) |
| Otimizador | controle (416) · plano (`*`) · registro (396, colapsa com o espaçador quando não há execução) |
| Histórico | tabela (`*`) · notas (320) |
| Configurações | painel único (máx. 1120), categorias (220) · formulário (máx. 760) |

Margem de página: 28px. Escala de espaçamento: 4 / 8 / 12 / 16 / 20 / 24 / 28.
Regiões cujo conteúdo cresce com o tempo (gráfico ao vivo, tabelas) ficam em
linha `*`. O painel de Configurações tem `MaxWidth` porque um formulário tem
largura útil finita: o que sobra ao lado é chão iluminado, e isso é intencional.

`MainWindow` mantém mínimo 1040×620 e abre maximizada.

## Elevation & Depth

Camada tonal e sombra curta fazem o trabalho. `Elevation1Shadow` ergue um
painel ou um botão primário; `Elevation2Shadow` um tooltip; `Elevation3Shadow`
o que realmente flutua (popup de combobox, `FloatingSurface`). Toda sombra
carrega deslocamento **e** desfoque, e é tingida de marrom profundo — uma
sombra neutra sob luz quente lê como um buraco recortado.

### Named Rules
**The No-Nested-Panel Rule.** Dentro de um painel, separação é espaço primeiro
e traço fino depois. O defeito estrutural das duas direções anteriores era
moldura dentro de moldura dentro de cartão: três bordas antes do primeiro dado.

## Shapes

`RadiusXs 4` (chip, etiqueta) · `RadiusSm 6` (campo, item de lista, opção
segmentada) · `RadiusMd 10` (botão, seletor, linha de tabela) · `RadiusLg 14`
(painel) · `RadiusXl 18` (superfície flutuante).

`RadiusPill` é permitido **apenas** no trilho/thumb do interruptor e no avatar
circular.

## Components

Todo controle interativo declara os sete estados: repouso, hover, pressionado,
foco de teclado, desabilitado, e — onde existir — marcado e inválido.
Alvo padrão: 40px de altura.

### Buttons
- **Primary**: 40px, `RadiusMd`, preenchido com o âmbar, erguido por `Elevation1Shadow`; press aplica `ScaleTransform 0.97` animado (entrada em `MotionMicro`, volta em `MotionControl`). É a única escala permitida na interface — escala dentro de linha/lista já causou itens deslizando sob o ponteiro, e um teste trava essa exceção.
- **Secondary**: contorno; hover levanta a superfície um degrau em vez de acender cor.
- **Danger ghost / Link / Icon**: variantes do contorno e do texto puro.

### Toggle
`ToggleSwitchStyle` segue o padrão Fluent do Windows 11: trilho 40×20 vazado
quando desligado, aceso pelo âmbar quando ligado, thumb de 12px que desliza e
cresce para 14px em hover. Reinventar um controle padrão num app de trabalho
custa confiança e não devolve nada.

### Segmented
O estado **marcado** é um bloco erguido (`Surface3Brush` + `Elevation1Shadow`),
não o mesmo preenchimento do hover. Na direção anterior selecionado e
sob-o-mouse pintavam a mesma cor, então era impossível saber qual opção estava
ativa sem tirar o ponteiro da tela. `SpectrumSelector` usa o mesmo vocabulário.

### Tables
`TableHeaderRowStyle` + `TableHeaderCellText` + `TableRowStyle`. Cabeçalho e
linha declaram **as mesmas larguras de coluna**, então alinham sem
`SharedSizeScope`. Sem listra zebrada, sem cartão por linha, sem barra lateral
colorida.

### Instruments
- **ProgressRailStyle**: trilho linear, sem easing — reflete o dado real; o indicador usa o gradiente do âmbar, então lê como luz avançando.
- **Escala de prontidão**: readout tabular grande + trilho + marcações 0/25/50/75/100.
- **Espinha de estações** (Otimizador): três marcas ligadas por conectores; a corrente é um disco âmbar cheio, as demais são anéis vazados.
- **LivePerformanceChart**: gráfico 2D leve dentro de um poço, em linha `*`.

### Surfaces
`PanelSurface` (o painel; `SheetSurface` é alias herdado) · `TitleBlockSurface`
(cabeçalho sem moldura) · `HeroSurface` + `HeroAccentRule` · `TitleAccentRule`
(a linha de luz sob o título) · `FieldSurface` (região dentro de um painel) ·
`InsetSurface` (poço) · `NotesColumnSurface` (painel irmão de apoio) ·
`FloatingSurface` (popup).

### Navigation
Rail esquerdo (`ui:NavigationView`, WPF-UI nativo). `ThemeManager` alimenta o
`ApplicationAccentColorManager` do WPF-UI com **exatamente** o mesmo âmbar de
`AccentBrush`: se os dois divergirem, o indicador da navegação acende numa cor
que não existe em nenhum outro lugar da interface.

## Motion

Durações e curvas vivem em `Themes/Tokens/Motion.xaml` e nenhuma Storyboard
nova deve usar valor fora dali. `App.ApplyMotionPolicyToDurationTokens` zera
essas durações na inicialização quando o Windows pede menos animação —
Storyboards declaradas dentro de `ControlTemplate` são congeladas e não
conseguem consultar `MotionPolicy` em runtime, então a política é aplicada na
fonte.

As curvas usam `PowerEase` de expoente alto, não as easings embutidas: as do
WPF gastam metade da duração perto do estado final, e uma transição fraca lê
como lentidão, não como suavidade.

### Named Rules
**The Never-Ease-In-On-Enter Rule.** Entrando ou reagindo → `EaseOut` forte.
Movendo-se na tela → `EaseInOut`. Saindo → `EaseIn` curto. Começar devagar
atrasa exatamente o momento em que o olho está observando.

## Do's and Don'ts

### Do:
- **Do** deixar o cabeçalho da página sobre o chão e agrupar só o que precisa de agrupamento.
- **Do** dar linha `*` ao que cresce com o tempo.
- **Do** usar `Grid` com coluna `*` quando um `TextBlock` precisa quebrar ao lado de um ícone — `StackPanel Orientation="Horizontal"` dá largura infinita ao filho e `TextWrapping` nunca dispara.
- **Do** declarar as mesmas larguras no cabeçalho e na linha de uma tabela.
- **Do** referenciar sempre um recurso de `Themes/`, nunca um hex ou `CornerRadius` literal.
- **Do** dar a todo estado colorido uma segunda pista de forma.

### Don't:
- **Don't** aninhar painel dentro de painel.
- **Don't** aplicar `Readout*`/`TickLabel` a uma frase.
- **Don't** usar `RadiusPill` fora do interruptor e do avatar.
- **Don't** reintroduzir geometria 3D, anel decorativo, glow ou gauge sem dado real por trás.
- **Don't** pintar a barra de título com `Transparent`: a janela usa backdrop Mica, e uma barra transparente deixa o Mica do **sistema** aparecer, então ela fica escura com o app em tema claro.
- **Don't** deixar a mesma informação em três telas. Limites de segurança vivem uma vez (Configurações › Sobre), o detalhamento de privacidade vive uma vez (Configurações › Privacidade).
- **Don't** subir o alfa da lâmpada acima de ~0x48 no epicentro: acima disso ela deixa de ser luz e vira uma mancha laranja.
