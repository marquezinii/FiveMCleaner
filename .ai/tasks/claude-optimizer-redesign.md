# Retrabalho completo do Otimizador (UI/UX/3D/animações)

- **Agente**: claude
- **Branch**: `ai/claude/optimizer-redesign` (base: `dev/proxima-versao`)
- **Worktree**: `../FiveMCleaner-optimizer-redesign`
- **Status**: pronto para integração

## Objetivo

Retrabalhar por completo a aba **Otimizador**, no mesmo espírito da
reconstrução já feita na **Visão geral**: nova composição, elementos em 3D,
animações com propósito e acabamento profissional — sem inventar métrica que o
produto não mede e sem alterar o fluxo de otimização.

## O que mudou

### Nova cena 3D — `Controls/OptimizerCore3D.cs` (novo)

Giroscópio desenhado em `Viewport3D` real: um icosaedro facetado no centro e
três toros (gerados proceduralmente, com normais explícitas para sombreamento
suave) girando em eixos e velocidades diferentes. O núcleo é grafite com
reflexo laranja — o emissivo é contido de propósito para não competir com o
anel de progresso, que é o dado da tela.

`Intensity` (0–1) controla velocidade e brilho; `IsLive` e o próprio
`IsVisible` param a cena quando a página não está em primeiro plano, no mesmo
padrão do `HoloCore3D` da Visão geral. A cena aparece nos dois estados:
em repouso alimentada pelo nível do perfil, em execução pelo progresso real.

### Página do Otimizador — `MainWindow.xaml`

- **Cabeçalho**: título grande em Bahnschrift, faixa laranja com `100% LOCAL`
  e o cabeçalho do plano, e uma **trilha de três etapas** (Preparar → Executar
  → Resultado) cuja etapa acesa vem do mesmo estado que decide qual cartão é
  exibido — não pode divergir do conteúdo.
- **Repouso**: cartão herói com a recomendação em texto à esquerda e o
  giroscópio 3D à direita, cercado por halo pulsante, anel orbital pontilhado e
  `ArcProgress` ligado ao nível do perfil; abaixo, escada de três degraus que
  traduz Leve → Médio → Agressivo sem número.
- **Indicadores**: três tiles (ações verificadas, impacto esperado, execução) e
  uma faixa dedicada de reversibilidade do plano.
- **Seleção de nível**: novo `ProfileCardStyle` — cartão inteiro clicável, com
  elevação real (sombra desfocada) no hover, brilho radial e barra de acento
  que abre ao selecionar, foco de teclado visível e a mesma escada de degraus.
- **Seu computador**: cinco tiles (CPU, GPU, memória, armazenamento, Windows)
  com ícones vetoriais do dicionário compartilhado, e os selos de detecção
  FiveM/GTA V agora com o mesmo check/X vetorial da Visão geral.
- **Execução**: cartão herói com o giroscópio acelerado pelo progresso,
  percentual sobre o núcleo, passo atual com ponto pulsante, passo anterior
  esmaecido, barra de progresso nova (`OptimizerProgressBarStyle`) com brilho
  que corre continuamente — sinal de "ainda trabalhando" quando o percentual
  fica parado numa ação longa.
- **Resultado**: selo de conclusão que entra animado uma única vez, próximo
  passo recomendado em faixa própria e as linhas do relatório redesenhadas.
- **Plano** e **comparação antes/depois** redesenhados; `ActionTemplate` e
  `ReportLineTemplate` reescritos (faixa lateral laranja, pastilha de ícone,
  hover sem deslocamento).

### Estilos — `Themes/Controls.xaml`

Novos: `StageChipStyle`, `ProfileCardStyle`, `ProfileStepStyle`,
`OptimizerProgressBarStyle`, `CoreHaloStyle`, `PlanActionCardStyle`,
`CompletionSealStyle`. Removido `ModeRadioStyle`, que só existia para os três
cartões de perfil e foi substituído por `ProfileCardStyle`.

**Decisão**: nenhuma animação usa `ScaleTransform`. A regra vem da revisão de
interação Fluent já registrada no projeto (escala deslocava listas sob o
ponteiro) e é verificada por teste de contrato. A barra de acento abre em
altura e o selo de conclusão entra subindo, em vez de crescerem.

### ViewModel — `MainViewModel.cs`

Três propriedades derivadas, nenhuma delas uma medida nova:

- `ProfileIntensity` / `ProfileIntensityPercent`: posição do perfil na escala
  Leve → Médio → Agressivo. **Não** é estimativa de ganho nem de FPS.
- `ProgressIntensity`: o mesmo `ProgressPercent`, mapeado para 0,3–1 para que a
  cena nunca pareça parada nos primeiros segundos de uma execução real.

### Ferramenta de verificação — `MainWindow.xaml.cs`

O smoke-test `--capture=` sempre fotografava a Visão geral. Adicionado
`--capture-page=Optimizer|History|Settings|Dashboard`, restrito ao caminho de
captura, que é o único jeito de conferir o Otimizador sem interação manual.

### Localização

Seis chaves novas nos três catálogos (en, pt-BR, es): `Optimizer.Level`,
`Optimizer.Safety`, `Optimizer.Detected`, `Optimizer.Stage.Prepare`,
`Optimizer.Stage.Run`, `Optimizer.Stage.Result`.

## Teste de contrato ajustado

`LocalizedInterfaceContractTests.FluentInteractionStyles_KeepListsStableAndKeyboardFocusVisible`
fixava em 2 as ocorrências de `IconCheck`/`IconClose` em `MainWindow.xaml` — os
dois selos de detecção da Visão geral. O Otimizador passou a usar os mesmos
selos vetoriais (antes eram só texto), então a contagem foi para 4. A intenção
original da asserção (os selos vêm do dicionário compartilhado, não de traçado
inline) continua preservada; só o número mudou, com comentário explicando.

## Validação

- Build Release sem avisos.
- 775 testes .NET aprovados.
- `dotnet format --verify-no-changes` aprovado.
- `scripts/Verify-Safety.ps1` aprovado.
- `git diff --check` limpo.
- Inspeção visual real pelo modo `--capture=` do próprio aplicativo, em janela
  maximizada, nos três estados: repouso, execução e resultado (os dois últimos
  fotografados com um patch temporário de visibilidade, revertido em seguida),
  além do plano expandido. Correções aplicadas a partir dessa inspeção: o
  emissivo do núcleo 3D estava saturado demais e competia com o anel de
  progresso; e os cartões de perfil não selecionados usavam `TileSurfaceBrush`,
  quase indistinguível do fundo — passaram para a superfície em vidro, com
  borda visível.

## Limitações e observações

- Os estados de execução e resultado foram fotografados com dados sintéticos
  (`--demo-synthetic`), em que o relatório vem vazio e o progresso em 0%. A
  conferência de uma execução real completa depende de rodar uma otimização de
  verdade na máquina.
- Sem mudança de versão, release, instalador, updater ou deploy.
