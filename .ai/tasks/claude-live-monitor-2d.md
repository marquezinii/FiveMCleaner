# Monitor ao vivo 2D no lugar da cena 3D

- **Agente:** Claude
- **Branch:** `ai/claude/live-monitor-2d` (worktree `../FiveMCleaner-ai-claude-live-monitor-2d`)
- **Objetivo:** substituir a cena 3D de CPU/GPU da Visão geral — que o usuário
  achou feia e travada — por um gráfico 2D limpo, leve, com atualização mais
  frequente que 2 s e leitura interativa sob o cursor.
- **Status:** pronto para integração.

## Mudanças

- **`Controls/LivePerformanceChart.cs`** (novo, `FrameworkElement`): gráfico 2D
  de CPU e GPU, grade tracejada em 25/50/75%, linha e área com gradiente. Não
  existe laço de animação: o controle só redesenha quando chega uma amostra ou
  quando o ponteiro se move. É essa a diferença de custo — a janela não precisa
  mais compor a 60 quadros por segundo continuamente.
  Ao passar o mouse: linha vertical, marcador nas duas séries e uma caixa com o
  tempo relativo (`-12s`) e os valores de CPU e GPU daquele ponto.
  Enquanto o histórico não enche, as amostras ocupam a largura inteira, em vez
  de deixar um trecho vazio à esquerda.
- **`Controls/PerformanceScene3D.cs`** removido, junto do `SceneFadeBrush`
  (`Palette.xaml` e `ThemeManager`), que só existia para o rodapé da cena.
  O `HoloCore3D` do medidor de prontidão foi preservado.
- **`MainWindow.xaml`**: a moldura do histórico passa a hospedar o gráfico 2D,
  com pincéis do tema (adapta a tema claro, o que a cena 3D não fazia) e os
  rótulos localizados de CPU/GPU repassados para a leitura sob o cursor.
- **`MainViewModel`**: coleta a cada 1 s (era 2 s) e histórico de 60 amostras
  (era 30), mantendo a janela de um minuto. Uma amostra por segundo é o piso
  útil: a leitura em si já usa uma janela PDH de ~300 ms.
- **Bug real encontrado e corrigido — CPU e disco sempre 0%**
  (`FiveMCleaner.Windows/Infrastructure/ResourceUsageInspector.cs`): a struct
  `PdhCounterValue` não declarava o campo `CStatus` de
  `PDH_FMT_COUNTERVALUE`, então a união do valor caía sobre ele e
  `DoubleValue` lia sempre 0 — em qualquer máquina, sem erro visível. O painel
  ao vivo, o anel de CPU e o de disco mostravam 0% permanentemente. A struct
  agora tem `CStatus` no offset 0 e a união no offset 8, e o status é validado
  (`PDH_CSTATUS_VALID_DATA`/`NEW_DATA`) antes de aceitar o valor. Medido nesta
  máquina antes: `cpu=0 disk=0`; depois: `cpu=43,2 disk=0,42`.
- **Testes**: `LivePerformanceChartTests` (mapeamento do ponto sob o cursor,
  janela ainda enchendo, escala vertical) e `ResourceUsageInspectorTests`
  (regressão do layout PDH: com o PC ocupado, a leitura de CPU não pode ser um
  zero constante). `LocalizedInterfaceContractTests` passou a exigir o gráfico
  2D e a ausência da cena 3D.

Nenhuma string localizada foi adicionada ou alterada.

**Fora do escopo, mas presente no commit:** `dotnet format` corrigiu
indentação pré-existente em `AppOptimizationService.cs` e
`LiveSystemMetricsProvider.cs` (blocos de inicializador e uma linha
desalinhados já em `dev/proxima-versao`). São mudanças só de espaçamento, sem
alteração de comportamento; foram mantidas porque `dotnet format
--verify-no-changes` — gate obrigatório de conclusão — falha sem elas.

## Testes

- `dotnet build -c Release`: sem avisos e sem erros.
- `dotnet test -c Release`: 696 aprovados, 0 falhas (690 antes; 6 novos).
- `dotnet format --verify-no-changes`: aprovado.
- `scripts/Verify-Safety.ps1`: aprovado.
- `git diff --check`: limpo.
- Inspeção visual pelo modo `--capture=` do aplicativo, em tela maximizada:
  histórico enchendo, histórico com ~25 amostras sob carga real de CPU e o
  estado com o ponteiro sobre o gráfico (linha, marcadores e caixa de leitura).

## Limitações

- A conferência do hover foi feita fixando o índice do ponteiro localmente para
  a captura; o movimento real do mouse depende de inspeção manual.
- O custo em repouso não foi medido com contador dedicado nesta rodada; a
  redução vem de o painel não manter mais animação contínua, e não de uma
  medição comparativa registrada aqui.
