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

## Terceira rodada: Discord, saudação por horário, fontes e mais copy

Pedido do usuário: ícone do Discord (site, app e Configurações), saudação
personalizada por horário na Visão geral, fontes mais distintas no app e
continuar removendo jargão. Antes de tocar em código, rodei
`ui-ux-pro-max --stack wpf` de novo (mesma skill da rodada anterior) para
checar convenções específicas de ícone/tipografia — sem achados novos além
dos já aplicados.

### Discord oficial (`https://discord.gg/bazcuQB9n6`)

- **Ícone vetorial próprio** (`Themes/Icons.xaml`, `IconDiscord`): ao
  contrário dos demais ícones do dicionário (só contorno), este é
  preenchido — a logomarca do Discord não existe como traço. `Fill` é
  `{DynamicResource TextBrush}`, que o `ThemeManager` já define como quase
  branco no escuro e quase preto no claro — satisfaz "branco no escuro,
  preto no claro" sem nenhuma lógica condicional nova.
- **Barra de título**: novo botão ícone (`DiscordButton`), com o mesmo
  padrão visual de hover do botão de conta ao lado, extraído para o estilo
  compartilhado `TitleBarIconButtonStyle` (`Themes/Controls.xaml`).
  Tooltip/nome de automação localizados ("Entre no nosso Discord oficial").
  Clique abre a URL via `Process.Start(UseShellExecute: true)` — o mesmo
  padrão já usado para o link do GitHub; o Windows decide se um cliente
  Discord instalado intercepta ou se abre no navegador.
- **Configurações → nova seção "Sobre"**: ícone e nome do app, tagline,
  versão + desenvolvedor (reaproveita o binding `AboutVersionDeveloper`, que
  já existia mas não estava em uso em nenhum lugar), link para o
  repositório (reaproveita `OpenRepository_Click`, também órfão até agora) e
  um cartão de CTA para o Discord com o mesmo ícone e o mesmo manipulador de
  clique da barra de título.
- **Site público**: adicionado nos dois lugares que geram HTML da versão
  pública —
  - `website/app/page.tsx` (Next.js/vinext + Cloudflare Worker, o site em
    desenvolvimento ativo do redesign do codex): ícone no cabeçalho e uma
    nova coluna "Comunidade" no rodapé.
  - `website/public-site/index.html` (HTML estático hospedado no GitHub
    Pages, com o próprio conjunto de testes que valida sua versão publicada):
    ícone no topbar e um link "Discord oficial" no rodapé.
  Nenhum conteúdo do codex foi revertido ou alterado — só adição. Os 3
  testes de `website/tests/rendered-html.test.mjs` (incluindo o que valida
  especificamente o site estático) e o lint continuam passando.

### Saudação por horário na Visão geral

- `MainViewModel.GreetingTitle`, computada em `RefreshGreeting()`: script
  simples de três faixas por hora local (06–12 bom dia, 12–18 boa tarde,
  18–06 boa noite), com o nome só quando há um `accountFirstName` presente.
  Recalculada na inicialização, a cada troca de idioma e sempre que a Visão
  geral volta a ficar visível (reaproveitando o hook que já existe para
  pausar/retomar a coleta ao vivo) — sem timer dedicado, já que o valor muda
  no máximo três vezes por dia.
- Colocada acima do título "Visão geral", como pedido.
- **Descoberta real durante a implementação**: o Worker só tinha
  `POST /account/profile` (grava no cadastro); não havia como o cliente ler
  de volta o nome depois — nem no login normal, nem na restauração
  silenciosa de sessão. Sem isso, a saudação nunca teria um nome fora do
  instante do cadastro. Adicionado:
  - Worker: `fetchAccountProfile` (`accountProfile.js`) e a rota
    `GET /account/profile`, autenticada do mesmo jeito que a de criação
    (`requireFirebaseUser`), lendo só o próprio perfil do UID do token —
    nunca de um id arbitrário. 2 testes novos.
  - App: `IAccountProfileService.FetchAsync`, implementado em
    `CloudflareAccountProfileService` e em `DisabledAccountProfileService`.
    `MainWindow` centralizou a construção do `IAccountProfileService` (antes
    recriado a cada clique no botão de conta) num campo único, e passou a
    escutar `StateChanged` com um handler nomeado que também dispara a
    sincronização do nome — cobre login, cadastro e restauração de sessão,
    porque os três já convergem para o mesmo evento no `FirebaseAuthService`
    existente. 4 testes novos no cliente.

### Fontes

- Títulos e valores de destaque (`SectionTitleStyle`, `KpiValueStyle` e as
  9 ocorrências inline de "Segoe UI Variable Display" no `MainWindow.xaml`
  e nas 3 janelas secundárias) passaram a usar `Bahnschrift` como primeira
  opção, com o Segoe UI Variable como fallback. Bahnschrift é uma fonte
  variável geométrica (estilo DIN) que já vem instalada no Windows 10/11 —
  zero download, zero risco de licença, contraste tipográfico real com o
  corpo de texto (que continua em Segoe UI Variable Text). Texto de corpo,
  legendas e o gráfico de desempenho não foram tocados.
- Confirmado por captura real: renderiza nítido, sem "tofu"/glifo ausente,
  peso Bold aplicado corretamente pelo eixo de variação da fonte.

### Mais jargão removido da Visão geral

- Removida a linha "ARQUITETURA DO SISTEMA x64" do cartão "Seu computador"
  — informação que não muda nenhuma decisão do jogador. A propriedade
  `SystemArchitectureLabel`, que só existia para essa linha, foi removida
  do view model (campo, propriedade e as 2 atribuições) em vez de deixada
  órfã.
- Frase da prontidão simplificada: "Sinal de capacidade baseado na memória
  disponível, processadores lógicos, espaço livre, placa de vídeo
  identificada, estado do cache e edição do FiveM. Não é benchmark de FPS
  nem promessa de desempenho." virou "Baseado na memória livre, no
  processador, no espaço em disco e no que foi encontrado do FiveM nesta
  máquina. Não é uma promessa de FPS." — mesma honestidade, sem
  "sinal de capacidade"/"processadores lógicos"/"benchmark".
- Indicador que eu mesmo adicionei na rodada anterior, "PRESSÃO DE
  DESEMPENHO", renomeado para "CARGA NO PC" — o conceito (Baixa/Moderada/
  Alta, mesma cor) continua, só o rótulo deixou de soar como termo técnico
  inventado.

Validação desta rodada: build Release da solução, 729 testes .NET (+4 em
relação à rodada anterior), 150 testes do Worker (+2), lint e 3 testes do
site (build + HTML renderizado + site estático), `dotnet format
--verify-no-changes`, `Verify-Safety.ps1` e `git diff --check` aprovados —
repetidos após cada mudança. Capturas reais confirmaram o ícone do Discord
na barra de título, o cartão "Sobre" (precisa rolar a página de
Configurações — ele está lá, só abaixo da dobra), a saudação acima de
"Visão geral", a fonte Bahnschrift renderizando e o texto simplificado.

## Observações para integração

- `ElevatedBrokerClientTerminalReadTests.ReadUntilTerminalAsync_ReportsLocalizedBrokerPhaseHeadlines`
  falhou **uma vez** em uma execução da suíte completa e passou isolada e em
  todas as execuções seguintes. É intermitente e independente desta tarefa
  (estado de cultura compartilhado entre testes paralelos); não foi alterado
  aqui.
- Nenhuma mudança de versão, changelog público, instalador, site ou telemetria.
