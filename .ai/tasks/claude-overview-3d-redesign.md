# Redesenho 3D da Visão geral

- **Agente:** Claude
- **Branch:** `ai/claude/overview-3d-redesign` (worktree `../FiveMCleaner-ai-claude-overview-3d`)
- **Objetivo:** refazer o visual, a UX e o movimento da aba Visão geral com
  elementos tridimensionais reais e animação profissional, preservando a
  estética escura, premium e minimalista, e substituir o gráfico simples de
  desempenho ao vivo por uma cena 3D elaborada.
- **Status:** pronto para integração.

## Mudanças

Três controles novos em `src/FiveMCleaner.App/Controls/`:

- **`PerformanceScene3D`** (`Viewport3D`): cena 3D ao vivo do último minuto.
  Duas fitas extrudadas (CPU laranja à frente, GPU azul atrás) crescem sobre um
  piso em grade com perspectiva, luz ambiente e direcional, e uma oscilação
  lenta de câmera. A malha tem topologia fixa e só as posições são reescritas,
  a ~20 quadros por segundo; os valores exibidos perseguem as amostras reais
  por interpolação, então o movimento é contínuo mesmo com coleta a cada 2 s.
  As amostras disponíveis ocupam sempre a largura inteira e os vértices sem
  amostra colapsam sobre o último ponto real (triângulos degenerados), o que
  evita a faixa zerada à esquerda enquanto o histórico ainda enche.
- **`HoloCore3D`** (`Viewport3D`): icosaedro facetado que gira atrás do medidor
  de prontidão. Vértices duplicados por face para sombreamento facetado; o
  brilho especular usa o laranja da marca, sem introduzir cor nova.
- **`ArcProgress`** (`FrameworkElement`): anel desenhado em `OnRender` com
  transição suave de valor (`CubicEase`, 760 ms). Usado no medidor de prontidão
  (anel completo) e nos quatro indicadores ao vivo (arco de 280°).

Interface e tema:

- `MainWindow.xaml`: a seção Dashboard foi reescrita. Cabeçalho com marcador de
  seção, bloco principal em vidro com brilho radial laranja e o medidor de
  prontidão em camadas (núcleo 3D, anel pontilhado em órbita, arco animado,
  disco de vidro com pontuação). O painel de desempenho ao vivo passou a ter
  quatro anéis animados e a cena 3D com legenda sobreposta; o resumo do
  computador ganhou linhas com ícone e agora ancora no rodapé a explicação da
  prontidão e o aviso de leitura local, aproveitando o espaço que sobrava.
- `Themes/Controls.xaml`: novos estilos `GlassCardStyle`, `HeroCardStyle`,
  `MetricTileStyle`, `SceneFrameStyle`, `CaptionStyle`, `LivePulseStyle` e
  `OrbitRingStyle`. As superfícies entram com opacidade e deslocamento animados
  quando a seção fica visível. Os `Freezable` de transformação ficam declarados
  no elemento, e não em `Setter.Value`, porque o estilo selado os congelaria.
- `Themes/Palette.xaml` e `ThemeManager`: novas superfícies em gradiente
  (`GlassSurfaceBrush`, `GlassBorderBrush`, `TileSurfaceBrush`,
  `HeroSurfaceBrush`, `HeroBorderBrush`, `SceneFadeBrush`), com variantes de
  tema claro aplicadas junto do hero existente.
- `MainViewModel`: `CpuUsagePoints`/`GpuUsagePoints` (`PointCollection` já
  projetada em coordenadas de tela) foram substituídos por `CpuUsageSeries` e
  `GpuUsageSeries` (`IReadOnlyList<double>`); a projeção agora é da cena.
  Nova propriedade `IsLiveMetricsActive`, para que a animação pare junto com a
  coleta ao sair da página ou minimizar para a bandeja.
- `LocalizedInterfaceContractTests`: o contrato da Visão geral passou a exigir a
  cena 3D, o núcleo 3D, as novas ligações de série e o vínculo de pausa, no
  lugar das medidas fixas do medidor circular anterior.

Nenhuma string localizada foi adicionada ou alterada; o escopo é apresentação.

## Testes

- `dotnet build -c Release`: sem avisos e sem erros.
- `dotnet test -c Release`: 636 aprovados, 0 falhas.
- `dotnet format --verify-no-changes`: aprovado.
- `scripts/Verify-Safety.ps1`: aprovado.
- `git diff --check`: limpo.
- Inspeção visual em janela maximizada pelo modo `--capture=` do aplicativo,
  com histórico vazio e com ~25 amostras reais coletadas.

## Custo medido e limitações

- Com a Visão geral em primeiro plano, o processo ficou em ~4,3–5,0% de CPU
  total desta máquina (12 threads), contra ~1,7% do build atual de
  `dev/proxima-versao`. Medições isolando a taxa de quadros da cena e a rotação
  do núcleo mostraram diferença desprezível: o custo vem de a janela compor a
  60 quadros por segundo enquanto qualquer animação está viva, não do 3D em si.
  Ao navegar para outra seção ou minimizar para a bandeja, a coleta, a cena, o
  núcleo, o anel em órbita e o pulso param, e o custo volta ao patamar anterior.
  Se esse consumo em repouso for indesejado, a decisão é de produto: reduzir a
  animação contínua é o único caminho eficaz.
- As cores das fitas 3D usam o laranja e o azul da paleta escura, também no
  tema claro; os elementos 3D não trocam de cor com o tema.
- A conferência foi feita em tela maximizada. A janela restaurada de 1160×680
  não foi capturada nesta rodada porque o aplicativo abre sempre maximizado.
