# Backlog de otimizações gráficas do Windows (proposto em 26/07/2026)

Este documento registra a classificação de um lote de otimizações gráficas
propostas pelo usuário para os perfis Leve/Médio/Agressivo, usando a legenda
abaixo.

## Atualização de 26/07/2026 (segunda rodada — implementação autorizada)

O usuário autorizou explicitamente implementar os itens abaixo. Três novas
ações entraram no catálogo (versão 11):

- `windows.gaming.gpu-preference-mismatch.diagnose` (👁, todos os perfis) —
  **implementado**. `GpuPreferenceMismatchDiagnosisAction`.
- `windows.gaming.fullscreen-optimizations.toggle` (🧪, **Agressivo
  apenas**, opt-in via `OptimizationOptionsDto.ToggleFullscreenOptimizationsExperiment`)
  — **implementado**. `FullscreenOptimizationsRegistryAction`.
- `windows.gaming.hags.toggle` (🧪, **Agressivo apenas**, opt-in via
  `OptimizationOptionsDto.ToggleHagsExperiment`, `RequiresRestart=true`,
  `RequiredPrivilege.Administrator` com `AttemptWithoutElevationFirst`) —
  **implementado**. `HagsToggleAction`.

**O que foi implementado, especificamente:** o mecanismo de aplicar/reverter
com segurança (registro, snapshot, rollback byte-a-byte). **O que NÃO foi
implementado:** a medição automática de frametime/latência antes-e-depois
com decisão automática de manter o melhor estado — isso exigiria orquestrar
um benchmark real (reaproveitando `WindowsGtaVBenchmarkRunner`) em torno de
cada toggle, o que é uma peça de trabalho maior e separada. Por ora, esses
dois itens 🧪 seguem o mesmo padrão já usado por outras opções "opt-in,
nunca automáticas" deste projeto (ex.: `ApplyGtaVRepairLaunchParameters`):
o usuário ativa, testa manualmente, e reverte pelo histórico se não gostar.

**Ainda sem UI**: como as demais opções opt-in já existentes
(`TerminateStuckFiveMProcess`, `RecreateFiveMLocalData`,
`ApplyGtaVRepairLaunchParameters` etc.), os dois novos toggles existem em
`OptimizationOptionsDto` mas ainda não têm checkbox no `MainWindow.xaml` —
consistente com o padrão já estabelecido neste projeto para opções opt-in
recém-adicionadas.

**Deliberadamente NÃO implementado nesta rodada** (continuam só como
backlog, pelos motivos técnicos/de segurança já registrados abaixo):
otimizações para jogos em janela do Windows 11 (sem API pública
confirmada), habilitar VRR programaticamente (mesma razão), troca
automática de frequência do monitor (risco real de tela preta sem hardware
variado para validar) e qualquer toggle de HDR/Auto HDR (mesma razão de
risco de exibição).

---

Este documento registra a classificação original (primeira rodada) do lote
completo, usando a legenda abaixo.

## Legenda

- ✅ **Automático seguro**: pode entrar nos modos normais.
- 🟡 **Opcional/condicional**: só aplicar após detectar compatibilidade ou com autorização.
- 🧪 **Experimental**: comparar antes e depois e reverter automaticamente.
- 🔧 **Reparo**: usar quando existe problema, não como otimização diária.
- 👁 **Diagnóstico**: o app analisa e recomenda, sem alterar.
- 🚫 **Não implementar**: perigoso, placebo ou tecnicamente mal fundamentado.

## 1. GPU de alto desempenho

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Registrar FiveM e GTA V nas preferências gráficas do Windows | ✅ | Todos | **Já implementado** — `windows.gaming.high-performance-gpu.prefer`, `AllProfiles`. |
| Selecionar a GPU de alto desempenho em notebooks com duas GPUs | ✅ | Todos | Coberto pela mesma ação acima — o Windows resolve automaticamente qual adaptador é "de alto desempenho". |
| Detectar quando o jogo está usando a integrada por engano | ✅ | Todos | **Implementado em 26/07/2026** como `windows.gaming.gpu-preference-mismatch.diagnose` — só leitura, cruza a detecção de duas GPUs com a preferência já configurada para o FiveM. |
| Restaurar a preferência original | ✅ | Todos | **Já implementado** — rollback padrão da ação existente. |

## 2. Otimizações para jogos em janela (Windows 11)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar Windows 11 compatível | 👁 | Todos | Pré-requisito de gate, não uma ação em si. |
| Ativar para FiveM em janela sem bordas | 🧪 | **Agressivo apenas** | Só se aplica quando o FiveM já está configurado em janela sem bordas (não força esse modo). Recurso pouco documentado publicamente pela Microsoft em termos de API estável — exige pesquisa de implementação antes de codar. |
| Permitir teste A/B | 🧪 | Agressivo | Faz parte do mesmo fluxo experimental acima — nunca uma mudança silenciosa. |
| Reverter se houver stutter, tearing ou incompatibilidade | ✅ | Agressivo | Parte do fluxo 🧪: reversão automática é obrigatória, não opcional. |
| Não aplicar cegamente em computadores com problemas conhecidos | 🟡 | Agressivo | Gate de compatibilidade antes de sequer oferecer o teste. |

**Decisão**: todo o recurso entra como **🧪 Experimental, opt-in, só no perfil Agressivo**, nunca como padrão automático em Leve/Médio.

## 3. Fullscreen Optimizations

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Manter ativado por padrão | ✅ | Todos | Não é uma ação — é a recomendação de **não mexer** por padrão (a própria Microsoft diz que o desempenho médio é igual ou melhor que fullscreen exclusivo). |
| Oferecer teste com desativação por aplicativo | 🧪 | **Agressivo apenas** | **Implementado em 26/07/2026** como `windows.gaming.fullscreen-optimizations.toggle` (toggle reversível; comparação automática de frametime ainda não implementada, ver nota no topo do documento). Nunca apresentado como otimização recomendada — é estritamente um teste de compatibilidade opt-in. |
| Medir frametime e latência nos dois estados | ✅ | Agressivo | Reaproveita a infraestrutura de benchmark/comparação já existente (`WindowsGtaVBenchmarkRunner`, `ResourceComparisonSnapshot`) em vez de criar um medidor novo do zero. |
| Restaurar o padrão se não houver melhora | ✅ | Agressivo | Reversão automática obrigatória, igual ao item 2. |

## 4. HAGS (Hardware-Accelerated GPU Scheduling)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Testar HAGS ligado e desligado | 🧪 | **Agressivo apenas** | **Implementado em 26/07/2026** como `windows.gaming.hags.toggle` (toggle reversível entre os dois estados). Exige reinício do Windows para ter efeito — não pode fazer parte de um fluxo "aplicar e já ver resultado" como as outras ações. |
| Registrar necessidade de reinicialização | ✅ | Agressivo | Usar o campo já existente `ActionMetadataDto.RequiresRestart`. |
| Manter resultado que oferecer melhor consistência | ✅ | Agressivo | Decisão automática dentro do fluxo 🧪, baseada na mesma comparação antes/depois do item 3. |
| Reverter facilmente | ✅ | Agressivo | Reversão do valor de registro (`HwSchMode`) já lido hoje só para diagnóstico. |
| Não apresentar HAGS como aumento garantido de FPS | 🚫 (regra de copy) | — | Regra de texto/UI, não uma ação: toda comunicação sobre HAGS deve deixar claro que o resultado varia por hardware/driver, nunca prometer ganho. |

**Decisão**: 🧪 Experimental, **Agressivo apenas**, com aviso de reinício explícito antes de aplicar.

## 5. Modo de Jogo (Game Mode)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar / Recomendar ativação / Ativar com backup | ✅ | Todos | **Já implementado** — `windows.gaming.game-mode.enable`, `AllProfiles`. |
| Oferecer teste desligado apenas quando houver incompatibilidade | 🧪 | **Agressivo apenas** | Novo: só oferecido quando o diagnóstico já identificou uma incompatibilidade conhecida com Modo de Jogo — nunca desligado por padrão nem em Leve/Médio. |

## 6. VRR (Variable Refresh Rate / G-Sync / FreeSync)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar suporte do monitor e da GPU | 👁 | Todos | **Parcialmente implementado**: `windows.gaming.session-settings.diagnose`/`windows.gaming.display-configuration.diagnose` já documentam que G-SYNC/FreeSync/VRR "não têm API pública sem driver do fabricante" e informam isso ao usuário em vez de adivinhar — essa limitação continua valendo. |
| Detectar se está desativado | 👁 | Todos | Mesma limitação acima: sem SDK do fabricante (NVIDIA/AMD/Intel), a leitura confiável do estado real de VRR não é garantida: manter como orientação, não fato. |
| Orientar ou habilitar VRR do Windows quando aplicável | 🟡 | **Pendente de pesquisa** | **Não decidido nesta rodada** — antes de implementar "habilitar", é preciso confirmar se existe um mecanismo público e documentado (o toggle de VRR em Configurações > Sistema > Vídeo tem uma chave de registro conhecida, mas isso precisa ser validado contra hardware real antes de virar código). Até essa pesquisa acontecer, o produto só **orienta** (👁), nunca **habilita** automaticamente. |
| Configurar perfil de FPS adequado | ✅ | Todos | **Já coberto** por `-frameLimit` em `gtav.legacy.launch-parameters.graphics.apply`. |
| Verificar se o monitor está conectado pela porta e cabo compatíveis | 👁 | Todos | Novo diagnóstico best-effort (ex.: alertar quando a conexão é HDMI 1.4 em vez de DisplayPort/HDMI 2.1, quando essa informação estiver disponível via EDID/registro) — nunca bloqueante, só informativo. |

## 7. Frequência do monitor

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar monitor de 144/165/180/240 Hz configurado em 60 Hz | 👁 | Todos | **Parcialmente implementado** em `windows.gaming.display-configuration.diagnose` (compara taxa configurada vs. máxima suportada). |
| Oferecer troca automática com confirmação | 🟡 | **Médio e Agressivo** | Sempre com o mesmo padrão de confirmação com contagem regressiva que o próprio Windows usa para mudança de resolução (para nunca deixar a tela travada numa configuração ruim) — nunca silencioso, nunca em Leve. |
| Restaurar se a tela não responder | ✅ | Médio e Agressivo | Parte obrigatória do fluxo acima, não opcional. |
| Identificar resolução que limita a frequência disponível | 👁 | Todos | Extensão do diagnóstico já existente. |
| Alertar sobre cabo ou porta possivelmente inadequados | 👁 | Todos | Mesmo caráter best-effort do item de VRR acima. |

## 8. Auto HDR e HDR

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Ativar somente por preferência visual | 🟡 | **Manual, nenhum perfil automático** | HDR é preferência visual, não ganho de desempenho — nunca faz parte de Leve/Médio/Agressivo; fica como opção manual em Configurações, fora dos perfis. |
| Desativar por aplicativo se causar problemas | 🔧 | **Manual, sob demanda** | Reclassificado de 🟡 para 🔧 (Reparo): só se usa quando já existe um problema relatado (ex.: cores erradas, crash), nunca como manutenção rotineira. |
| Não classificar HDR como otimização de FPS | 🚫 (regra de copy) | — | Regra de texto/UI: toda comunicação sobre HDR/Auto HDR deve deixar claro que é ajuste visual, nunca prometer FPS. |

## Resumo por perfil

- **Leve**: nada novo entra aqui além do que já existe hoje (GPU de alto desempenho, Modo de Jogo) — este lote não adiciona itens automáticos ao perfil Leve, mantendo-o o mais conservador.
- **Médio**: ganha a troca de frequência do monitor com confirmação (🟡) além do que já existe.
- **Agressivo**: ganha todos os itens 🧪 (janela sem bordas Win11, Fullscreen Optimizations por app, HAGS, Modo de Jogo desligado condicional) e a troca de frequência do monitor.
- **Diagnóstico (👁, todos os perfis, sem alterar nada)**: detecção de GPU integrada por engano, suporte/estado de VRR, cabo/porta inadequados, frequência do monitor abaixo do máximo.
- **Manual, fora de qualquer perfil automático**: HDR/Auto HDR (🟡 ativar por preferência, 🔧 desativar em caso de problema).
- **Pendente de pesquisa antes de qualquer código**: habilitar VRR do Windows programaticamente — só vira ✅/🟡 codificável depois de confirmar um mecanismo público e testá-lo em hardware real.

## Próximos passos

1. Nenhuma dessas ações foi implementada nesta rodada — este documento é o guia de priorização para quando a implementação começar.
2. Antes de implementar qualquer item 🧪, revisar `docs/safety.md` para garantir que o padrão de comparação antes/depois e reversão automática siga o mesmo modelo já usado por `OptimizationComparisonResult`/`ComputeRegressionReasonKeys`.
3. A pesquisa sobre o mecanismo de habilitar VRR via Windows precisa ser registrada em `docs/research.md` (Fato/Inferência/Fora de escopo) antes de qualquer código ser escrito para esse item específico.
