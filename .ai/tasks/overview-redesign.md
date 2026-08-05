# Reconstrução da Visão geral

- **Agente**: claude
- **Branch**: `ai/claude/overview-redesign` (worktree `../FiveMCleaner-overview-redesign`)
- **Base**: `dev/proxima-versao` (`106eede`)
- **Objetivo**: reconstruir a aba Visão geral — UX/UI, hierarquia visual, ícones
  vetoriais próprios, mais informação real e fim das zonas mortas.
- **Status**: concluída, pronta para integração.

## Mudanças

### Ícones vetoriais próprios (`Themes/Icons.xaml`, novo)

Toda a Visão geral deixou de usar a fonte de glifos `Segoe MDL2 Assets`. Foram
desenhados 22 ícones em uma grade única de 24×24, somente contorno, com
espessura/junção/ponta arredondadas padronizadas em `VectorIconStyle` (mais
`VectorIconSmallStyle` e `VectorIconLargeStyle`). Os glifos MDL2 variavam de
peso, alinhamento óptico e estilo entre si e não acompanhavam a cor nem a
espessura do resto da interface. `GeometryKeyConverter` (novo, em
`Converters.cs`) permite que o view model nomeie um ícone por chave sem
depender de tipos gráficos do WPF — mesmo padrão do `BrushKeyConverter` já
existente.

O restante do aplicativo (Otimizador, Histórico, Configurações) não foi tocado
e continua com os glifos atuais; trocar tudo estaria fora do escopo pedido.

### Página reconstruída (`MainWindow.xaml`)

- **Cabeçalho**: linha de contexto (`DIAGNÓSTICO LOCAL` + horário da última
  varredura) acima do título, e a ação de reanalisar com ícone vetorial. O
  número exibido agora tem idade explícita.
- **Herói**: os selos de detecção ganharam rótulo de seção, quebra responsiva
  (`WrapPanel`) e check/X vindos do dicionário de ícones. Duas ações de
  navegação (Abrir Otimizador, Ver histórico) — a Visão geral continua sem
  executar otimização.
- **Faixa de indicadores (nova)**: quatro números da própria varredura local —
  pressão de desempenho (colorida por nível), núcleos lógicos, memória livre
  (com o total) e tamanho do cache do FiveM. O cache é o número que mais
  explica por que o otimizador tem trabalho a fazer e não aparecia em lugar
  nenhum.
- **Desempenho ao vivo**: cinco leituras (CPU, GPU, memória, disco, rede) em vez
  de quatro; a rede saiu de uma etiqueta flutuante sobre o gráfico e virou um
  tile próprio. O tile de memória mostra o valor absoluto (`15,8 / 31,9 GB`).
  Abaixo do gráfico, média e pico das **mesmas** amostras desenhadas — responde
  "esteve alto?" sem depender de o usuário ter olhado na hora certa.
- **Seu computador**: ganhou a linha de Windows + arquitetura do sistema, que
  o diagnóstico já coletava e a página não mostrava.
- **Última otimização (novo cartão)**: fecha a página com perfil, data e
  resultado da última execução real e um atalho para o histórico. Sem
  histórico, o cartão explica esse estado em vez de sumir e deixar um vazio.

### View model e serviços

- `MainViewModel`: novas propriedades derivadas de dados que já existiam —
  `LastScanLabel`, `PerformancePressureLabel`/`BrushKey`, `LogicalProcessorLabel`,
  `AvailableMemoryLabel`, `FreeDiskLabel`, `LegacyCacheLabel`,
  `SystemArchitectureLabel`, `MemoryUsageDetailLabel`, `CpuTrendLabel`,
  `GpuTrendLabel`, `LastOptimization*` e `HasLastOptimization`. Nenhum valor é
  estimado: cache abaixo de 1 GiB aparece em MiB e ausência de cache reporta
  "nenhum", não zero.
- `LiveSystemMetricsSnapshot`: dois campos opcionais novos (memória usada e
  total em GiB), lidos do inspetor que já era consultado — sem coleta extra.
- `StreamingReadinessDisplayItem`: `IconGlyph` virou `IconKey` (chave de ícone
  vetorial) e ganhou `ToneBrushKey`, para o ícone refletir o tom do check.
- `LivePerformanceChart`: nova `NowLabel`. A leitura sob o cursor tinha a
  palavra "agora" fixa em português, que aparecia assim também em inglês e
  espanhol.
- 29 chaves novas em cada um dos três catálogos (`Strings`, `pt-BR`, `es`).

### Bug real encontrado e corrigido

No medidor de prontidão, a pontuação e a escala `/100` eram dois `TextBlock`
irmãos dentro do núcleo de largura fixa. Em escalas fracionárias de DPI (1,5×),
o número era medido curto e perdia o último dígito: **97 aparecia como 9**,
enquanto o anel desenhava 97%. Confirmado por captura em janela restaurada e
por leitura direta do valor (`97|Excelente`). Os dois viraram um único
`TextBlock` com `Run`s e medição `Ideal`; recapturado no mesmo DPI, agora
mostra `97 /100`. O defeito é anterior a esta tarefa (o núcleo já era de
largura fixa), mas estava exatamente no bloco reconstruído.

## Testes

- Build Release da solução: sem avisos, sem erros.
- 725 testes .NET aprovados.
- `dotnet format --verify-no-changes`, `scripts/Verify-Safety.ps1` e
  `git diff --check` aprovados.
- Inspeção visual real via `--capture=` do próprio aplicativo, em janela
  maximizada e restaurada 1160×680, com métricas ao vivo já estabilizadas
  (captura com atraso temporário, revertido antes do commit).
- Contrato de interface atualizado: `Dashboard_FocusesOnStatus...` passou a
  cobrir a faixa de indicadores, média/pico, o cartão de última otimização e a
  ausência de `Segoe MDL2 Assets` na página; `FluentInteractionStyles_...`
  passou a verificar o check/X vetorial no dicionário compartilhado, no lugar
  do traçado inline duplicado.

## Segunda rodada: auditoria com skills de design

Depois da primeira entrega, rodei as skills `frontend-design` e `ui-ux-pro-max`
(banco de regras de UI/UX, 98 diretrizes + checklist de pré-entrega) contra o
que já estava pronto, em vez de só contra a ideia inicial. Achados reais:

- **Tokens de ícone inconsistentes**: os dois selos de detecção (FiveM/GTA V)
  tinham `StrokeThickness="1.9"` embutido, fora dos três tamanhos padronizados
  em `Icons.xaml`; e seis ícones de legenda (CPU/GPU/memória/disco/rede/
  prontidão) usavam um `Width="12" Height="12"` improvisado por cima do
  `VectorIconSmallStyle` de 14px. Ambos corrigidos: os selos passaram a herdar
  `VectorIconSmallStyle`, e o tamanho de legenda virou um quarto token nomeado,
  `VectorIconCaptionStyle` (12px, traço 1,8), em vez de um número mágico
  repetido seis vezes.
- **Contraste**: `TextSubtleBrush` (#676C76) sobre o fundo do app mede ~3,5–3,7:1
  — passa no piso de 3:1 para texto secundário, mas é um token global existente
  usado em todo o app (não só na Visão geral); não alterado aqui por estar fora
  do escopo desta tarefa.
- **DPI awareness ausente (bug real, corrigido)**: a checagem específica de
  stack `wpf` da skill aponta que o app não declarava `PerMonitorV2` em nenhum
  lugar — `app.manifest` não tinha bloco `dpiAwareness`. Isso é consistente com
  o defeito do medidor já corrigido nesta tarefa (pontuação perdendo o último
  dígito em DPI fracionário): sem `PerMonitorV2`, o WPF roda System-DPI-aware e
  o Windows faz upscale de bitmap da janela inteira ao invés de deixar o WPF
  recalcular o layout na escala real do monitor.
  - Primeira tentativa, `ApplicationHighDpiMode` no `.csproj` (sugestão do
    próprio aviso do compilador `WFO0003`, que aparece porque o projeto usa
    WinForms para a bandeja do sistema): **não teve efeito nenhum** — essa
    propriedade só é lida pelo `Program.Main` gerado pelo designer do WinForms
    (`ApplicationConfiguration.Initialize()`), que este projeto não usa (quem
    inicia é o `App.xaml` do WPF). Confirmado inspecionando o binário: nenhuma
    entrada de DPI foi gravada no manifesto embutido.
  - Correção efetiva: `dpiAwareness=PerMonitorV2` direto no `app.manifest`
    (onde a `HwndSource` do WPF realmente lê), com o aviso `WFO0003` suprimido
    deliberadamente via `NoWarn` no `.csproj` — documentado inline nos dois
    arquivos com o porquê. Confirmado por grep no `.exe` compilado que
    `PerMonitorV2`/`dpiAwareness` agora estão embutidos, e por captura visual
    (`--capture=`) que a página renderiza igual, com a pontuação `97 /100`
    correta.

Validação desta rodada: build Release e suíte completa (725 testes) repetidos
depois de cada mudança, `dotnet format --verify-no-changes` (precisou de uma
correção de final de linha, aplicada), `Verify-Safety.ps1` e captura visual
real do app compilado — todos aprovados.

## Observações para integração

- `ElevatedBrokerClientTerminalReadTests.ReadUntilTerminalAsync_ReportsLocalizedBrokerPhaseHeadlines`
  falhou **uma vez** em uma execução da suíte completa e passou isolada e em
  todas as execuções seguintes. É intermitente e independente desta tarefa
  (estado de cultura compartilhado entre testes paralelos); não foi alterado
  aqui.
- Nenhuma mudança de versão, changelog público, instalador, site ou telemetria.
