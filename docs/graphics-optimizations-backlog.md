# Backlog de otimizações gráficas do Windows (proposto em 26/07/2026)

Este documento registra a classificação de um lote de otimizações gráficas
propostas pelo usuário para os perfis Leve/Médio/Agressivo, usando a legenda
abaixo.

## Atualização de 26/07/2026 (quinta rodada — implementação do lote AMD)

O usuário mandou o lote "Driver e perfil AMD" já pedindo implementação
direta. Mesma conclusão técnica da rodada NVIDIA: a AMD também não publica
uma API pública oficialmente suportada para escrever no perfil por
aplicativo do AMD Software: Adrenalin Edition — então quase toda a lista
🟡 caiu em 🚫 pelo mesmo motivo, não por falta de tempo.

**Implementado, generalizando as duas ações já existentes para os dois
fabricantes** (nenhuma ação nova de catálogo, `CurrentVersion` continua
`12`):

- `windows.gaming.gsync.guide` foi generalizada: detecta o fabricante da
  GPU (`IGpuVendorInspector`) e nomeia o painel certo — "NVIDIA Control
  Panel (Configurar G-SYNC)" ou "AMD Software: Adrenalin Edition
  (FreeSync)" — em vez de mencionar só NVIDIA. Nome/descrição da ação no
  catálogo atualizados para "G-SYNC/FreeSync/VRR".
- `windows.system.driver-versions.diagnose`
  (`DriverVersionsDiagnosisAction`/`ClassifyOldDrivers`) já era genérico
  por vendor desde a rodada anterior (lê `Win32_PnPSignedDriver` de
  qualquer fabricante) — cobre "detectar versão do Adrenalin" e "alertar
  sobre atualização" para AMD sem nenhuma mudança de código.
- `windows.gaming.gpu-vendor.detect` (`GpuVendorDetectionAction.Classify`)
  ganhou links diretos de download por fabricante detectado (nvidia.com/
  drivers, drivers.amd.com, Intel download center) — cobre "direcionar ao
  driver oficial".
- `windows.system.driver-reinstall.guide`
  (`GuidedDriverReinstallAction`) já era genérico (DDU funciona para
  NVIDIA e AMD); o texto passou a mencionar isso explicitamente — cobre
  "reinstalação limpa guiada".

**Classificado 🚫 (mesma razão da rodada NVIDIA — sem API pública
suportada)**: Radeon Anti-Lag, Radeon Chill, Radeon Boost, Radeon Image
Sharpening, Radeon Super Resolution, Enhanced Sync, limite de FPS pelo
driver, perfil de desempenho por aplicativo, desativar overlay/gravação
automaticamente, e AMD Fluid Motion Frames (mesmo com a moldura 🧪 do
pedido original — a limitação técnica de escrita é a mesma, não muda por
"comparar antes e depois"). Overclock e undervolt automáticos continuam
fora de escopo por definição do produto.

## Atualização de 26/07/2026 (quarta rodada — implementação do lote NVIDIA/G-SYNC)

O usuário autorizou implementar tudo que desse do lote "Driver e perfil
NVIDIA" + G-SYNC (terceira rodada, classificada abaixo nas seções 9 e 10).
Catálogo subiu para `CurrentVersion = 12` com 2 ações novas e 2 diagnósticos
existentes ampliados:

- `windows.gaming.gsync.guide` (👁, todos os perfis, `ActionOptionGate.Always`)
  — **implementado**. `GSyncGuidanceDiagnosisAction`: orienta ativar
  G-SYNC/VRR pelo NVIDIA Control Panel/monitor e sugere um limite de FPS
  (alguns quadros abaixo da taxa máxima detectada) usando o `-frameLimit`
  já existente. Nunca ativa nada sozinho.
- `windows.system.driver-reinstall.guide` (🔧, opt-in via
  `OptimizationOptionsDto.GuideDriverReinstall`, todos os perfis quando
  ativado) — **implementado**. `GuidedDriverReinstallAction`: mostra os
  passos oficiais (DDU + instalador do fabricante), nunca baixa/instala/
  remove nada.
- **`DiagnoseDriverVersions` ampliado**: `DriverVersionInfo` ganhou
  `DriverDate` (lido de `Win32_PnPSignedDriver.DriverDate`, mesma data do
  Gerenciador de Dispositivos), e `DriverVersionsDiagnosisAction.ClassifyOldDrivers`
  alerta quando o driver de vídeo está há mais de 18 meses sem atualização
  — sinal objetivo (data), não um palpite pela string de versão.
- **`DetectOverlaysAndCaptureSoftware` ampliado**: quando "NVIDIA Share /
  ShadowPlay" é detectado (o processo real por trás do Instant Replay), a
  mensagem agora menciona que Instant Replay pode estar ativo e que
  filtros do Freestyle também podem estar em uso — sem inventar um sinal
  de detecção que não existe para o Freestyle isoladamente.

**O que NÃO foi implementado, com justificativa técnica (não decisão
arbitrária)**: praticamente toda a lista de "configurações possíveis" do
perfil 3D por aplicativo da NVIDIA (baixa latência, limite de FPS pelo
driver, G-SYNC por aplicativo, Shader Cache Size, Texture Filtering
Quality, Threaded Optimization, NVIDIA Image Scaling, DSR, gerenciamento de
energia por app, criar perfil por aplicativo, desativar overlay
automaticamente) — **não existe API pública e oficialmente suportada da
NVIDIA para escrever essas configurações**. Isso não é uma lacuna de tempo
ou prioridade: é a mesma política já registrada em `docs/safety.md`
("ajustes de perfil 3D só pelo painel oficial do fabricante") aplicada
consistentemente. Ver seção 9 abaixo para o detalhamento item a item.

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

## 9. Driver e perfil NVIDIA (lote proposto em 26/07/2026, terceira rodada)

**Contexto que decide quase toda essa seção**: a NVIDIA não publica uma API
pública e documentada para escrever no perfil 3D por aplicativo (o que o
NVIDIA Control Panel/NVIDIA App chama de "configurações do programa"). As
ferramentas que fazem isso (nvidiaProfileInspector e similares) usam a
NVAPI de forma não documentada/não suportada oficialmente. Isso já é a
política registrada em `docs/safety.md` e reforçada pelo próprio texto do
`GpuVendorDetectionAction`: *"Ajustes de perfil 3D devem ser feitos apenas
pelo painel oficial do fabricante... o FiveMCleaner não escreve nem
sobrescreve esses perfis."* Quase toda a lista de "configurações possíveis"
proposta cai direto nessa regra já existente, não é uma decisão nova.

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar versão do driver | ✅ | Todos | **Já implementado** — `windows.system.driver-versions.diagnose`. |
| Alertar sobre driver muito antigo | ✅ | Todos | **Implementado em 26/07/2026** — `DriverVersionsDiagnosisAction.ClassifyOldDrivers`, limiar de 18 meses pela data real do driver (`DriverDate`), não pela string de versão. |
| Direcionar ao driver oficial | ✅ | Todos | Só um link/mensagem para nvidia.com/drivers — nunca baixa nem instala nada sozinho. |
| Oferecer reinstalação limpa guiada quando houver corrupção | 🔧 | Manual, sob demanda | **Implementado em 26/07/2026** — `windows.system.driver-reinstall.guide`/`GuidedDriverReinstallAction`, opt-in, mostra os passos oficiais (DDU + instalador do fabricante), nunca executa nada sozinho. |
| Criar perfil por aplicativo, quando houver API ou integração segura | 🚫 | — | Não existe API pública/suportada para isso (ver contexto acima). "Quando houver integração segura" nunca se confirmou nas pesquisas feitas até agora. |
| Modo de gerenciamento de energia: preferir desempenho máximo para o FiveM | 🚫 | — | Configuração de perfil 3D por aplicativo — mesma limitação de API. |
| Modo de baixa latência: ligado | 🚫 | — | Mesma limitação de API — é um valor do perfil 3D por aplicativo (`OGL_LOW_LATENCY_MODE`/equivalente), não documentado publicamente para escrita externa seguro. |
| Modo de baixa latência: Ultra, com benchmark | 🚫 | — | Mesma limitação técnica que o item acima; a moldura "🧪 experimental com benchmark" não muda o fato de não haver como escrever o valor com segurança. |
| Limite de FPS pelo driver | 🚫 | — | Mesma limitação de API. **Alternativa já implementada**: `-frameLimit` em `gtav.legacy.launch-parameters.graphics.apply` limita o FPS no nível do próprio jogo, sem depender do driver. |
| Sincronização vertical conforme estratégia de VRR | 🚫 | — | Mesma limitação de API (perfil 3D por aplicativo). |
| G-SYNC em tela cheia ou também em janela | 🚫 | — | Mesma limitação de API — ver também a seção G-SYNC abaixo. |
| Shader Cache Size | 🚫 | — | Mesma limitação de API. |
| Texture Filtering Quality | 🚫 | — | Mesma limitação de API. |
| Threaded Optimization | 🚫 | — | Mesma limitação de API. |
| NVIDIA Image Scaling | 🚫 | — | Mesma limitação de API; também altera a imagem renderizada (reamostragem), o que é uma escolha visual do usuário, não uma otimização segura por padrão. |
| Desativar DSR se estiver causando resolução excessiva | 🚫 | — | Mesma limitação de API. |
| Desativar overlay da NVIDIA durante o jogo, se não utilizado | 👁/🟡 | Todos (orientação apenas) | Sem mecanismo de escrita confirmado e documentado; o produto pode, no máximo, **orientar** o usuário a desativar manualmente no NVIDIA App, nunca alternar sozinho. |
| Detectar gravação instantânea ativa (Instant Replay) | 👁 | Todos | **Implementado em 26/07/2026** — o detector de overlays já existente (`windows.gaming.overlays.detect`) já reconhecia o processo "NVIDIA Share" (o processo real do Instant Replay/ShadowPlay); a mensagem foi ampliada para mencionar isso explicitamente. |
| Detectar filtros Freestyle ativos | 👁 | Todos | **Parcialmente implementado** junto com o item acima — sem um sinal de processo isolado para o Freestyle, a mensagem só menciona que filtros podem estar em uso quando o overlay NVIDIA é detectado, nunca afirma isso como fato. |
| Não alterar perfil global da GPU | 🚫 (regra já vigente) | — | Já é a política documentada em `docs/safety.md`; este item só confirma a regra, não muda nada. |
| Não apagar shader cache a cada execução | 🚫 (regra já vigente) | — | Nunca foi cogitado como limpeza automática neste projeto; confirmado aqui para constar. |
| Não forçar overclock | 🚫 (regra já vigente) | — | Fora de escopo deste produto por definição (`docs/safety.md`). |
| Não modificar voltagem ou power limit automaticamente | 🚫 (regra já vigente) | — | Mesma regra. |

## 10. G-SYNC (lote proposto em 26/07/2026, terceira rodada)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar monitor compatível | 👁 | Todos | Mesma limitação já registrada na seção 6 (VRR): sem SDK do fabricante, a detecção é best-effort, nunca um fato garantido. |
| Orientar habilitação | 👁 | Todos | **Implementado em 26/07/2026** — `windows.gaming.gsync.guide`/`GSyncGuidanceDiagnosisAction`. Orienta, nunca habilita sozinho — habilitar G-SYNC é uma opção do NVIDIA Control Panel (perfil por aplicativo/global), sujeita à mesma limitação de API da seção 9. |
| Detectar se funciona apenas em tela cheia ou também em janela | 👁 | Todos | Best-effort, mesma limitação. |
| Oferecer indicador de verificação | 👁 | Todos | O indicador on-screen do G-SYNC é uma opção do driver NVIDIA — o produto pode no máximo orientar o usuário a ativá-lo manualmente, não ativá-lo sozinho. |
| Criar limite de FPS compatível com a faixa do monitor | ✅ | Todos (onde já implementado) | **Já coberto** por `-frameLimit` em `gtav.legacy.launch-parameters.graphics.apply` — não depende do driver NVIDIA, funciona com qualquer GPU. |

## 11. Driver e perfil AMD (lote proposto e implementado em 26/07/2026, quinta rodada)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar versão do Adrenalin | ✅ | Todos | **Já coberto** — `windows.system.driver-versions.diagnose` é vendor-neutro, lê qualquer driver de vídeo via WMI. |
| Alertar sobre atualização | ✅ | Todos | **Já coberto** — `ClassifyOldDrivers` (limiar de 18 meses pela data real do driver) já era vendor-neutro. |
| Direcionar ao driver oficial | ✅ | Todos | **Implementado em 26/07/2026** — `GpuVendorDetectionAction.Classify` agora inclui o link de download por fabricante detectado. |
| Reinstalação limpa guiada | 🔧 | Manual, sob demanda | **Já coberto** — `windows.system.driver-reinstall.guide` já era genérico (DDU funciona para AMD e NVIDIA); texto ajustado para mencionar isso explicitamente. |
| Radeon Anti-Lag | 🚫 | — | Configuração de perfil por aplicativo do Adrenalin; sem API pública suportada. |
| Radeon Chill desligado quando limitar FPS sem intenção | 🚫 | — | Mesma limitação de API — mesmo com a condição "sem intenção", não há como ler/escrever esse estado com segurança de fora do Adrenalin. |
| Radeon Boost com resolução dinâmica | 🚫 | — | Mesma limitação de API. |
| Radeon Image Sharpening | 🚫 | — | Mesma limitação de API. |
| Radeon Super Resolution, se compatível | 🚫 | — | Mesma limitação de API; também é uma escolha visual (reamostragem), igual à NVIDIA Image Scaling. |
| FreeSync | 👁 | Todos | **Implementado em 26/07/2026** — coberto pela generalização de `windows.gaming.gsync.guide`, que já orienta especificamente pelo AMD Software: Adrenalin Edition quando detecta GPU AMD. |
| Enhanced Sync, com teste | 🚫 | — | Mesma limitação de API — a moldura "com teste" não resolve a falta de um jeito seguro de escrever o valor. |
| Limite de FPS | 🚫 | — | Mesma limitação de API. **Alternativa já implementada**: `-frameLimit` em `gtav.legacy.launch-parameters.graphics.apply`, independente do driver. |
| Perfil de desempenho por aplicativo | 🚫 | — | Mesma limitação de API — é exatamente "criar perfil por aplicativo" da seção 9, com o mesmo motivo. |
| Desativar overlay e gravação instantânea | 🚫 | — | Mesma limitação de API para alternar automaticamente; o app já **detecta** overlays em execução (`windows.gaming.overlays.detect`), só não os fecha. |
| AMD Fluid Motion Frames (AFMF) | 🚫 | — | Sem API pública para alternar, apesar da moldura 🧪 do pedido original. Se implementado manualmente pelo usuário no Adrenalin, a orientação sobre os efeitos colaterais (frames interpolados, não reais; possíveis artefatos/latência) fica registrada aqui como texto de referência, não como funcionalidade do app. |
| Não ativar automaticamente tecnologias sem verificar compatibilidade | 🚫 (regra já vigente) | — | Já é a postura do produto em toda ação opt-in/experimental existente. |
| Não aplicar overclock ou undervolt automático | 🚫 (regra já vigente) | — | Mesma regra já registrada para a NVIDIA (seção 9), agora confirmada também para AMD. |

## 12. Driver Intel e notebooks híbridos (lote proposto e implementado em 26/07/2026, sexta rodada)

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Detectar Intel Arc ou Intel integrada | ✅ | Todos | **Já coberto** — `windows.gaming.gpu-vendor.detect` e `windows.gaming.gpu-preference-mismatch.diagnose` já distinguem GPU integrada (`Intel(R) UHD/HD/Iris`) de dedicada; uma Arc não bate com esses marcadores e cai naturalmente no lado "dedicada". |
| Detectar driver | ✅ | Todos | **Já coberto** — `windows.system.driver-versions.diagnose` é vendor-neutro (lê qualquer driver de vídeo via WMI). |
| Direcionar para atualização oficial | ✅ | Todos | **Já coberto** — `GpuVendorDetectionAction.Classify` já inclui o link de download da Intel desde a rodada NVIDIA. |
| Forçar FiveM para a GPU dedicada pelo Windows | ✅ | Todos | **Já implementado antes desta rodada** — `windows.gaming.high-performance-gpu.prefer` já faz exatamente isso (registra preferência de GPU de alto desempenho para FiveM/GTA V). |
| Detectar notebook em modo economia | 👁 | Todos | **Implementado em 26/07/2026** — `windows.gaming.hybrid-laptop.diagnose`, via `IPowerStatusProvider.IsBatterySaverActive()` (novo) + estado de CA/bateria já existente. |
| Detectar MUX switch quando exposto pelo fabricante | 👁 | Todos | **Implementado como proxy honesto** — detecta se um utilitário conhecido do fabricante que expõe MUX switch está instalado (Armoury Crate, MSI Center, Lenovo Vantage, Dell Power Manager, Alienware/HP/Acer/Gigabyte). Não confirma a existência do MUX em si, só que a ferramenta que o controlaria está presente. |
| Recomendar modo GPU dedicada no software do notebook | 👁 | Todos | **Implementado junto com o item acima** — a mesma ação orienta usar o utilitário detectado para ativar o modo de GPU dedicada. |
| Recomendar conectar carregador | 👁 | Todos | **Implementado em 26/07/2026** — mesma ação, mensagem quando `IsOnAcPower()` é falso. |
| Detectar limite térmico ou de potência | 👁 | Todos | **Já coberto**, sem nenhuma mudança — `safety.throttling-signal.diagnose`/`safety.thermal.diagnose` já existem; a nova ação não duplica essa cobertura. |
| Ativar perfil Performance do fabricante | 🚫 | — | Mesma limitação técnica das seções 9/10/11: os utilitários de notebook (Armoury Crate, MSI Center, Lenovo Vantage etc.) não publicam API pública oficialmente suportada para ativar perfis de desempenho por fora do próprio app do fabricante. |
| Não tentar controlar MUX ou BIOS por métodos genéricos não documentados | 🚫 (regra já vigente) | — | Confirmado nesta rodada; é a mesma razão pela qual "detectar MUX switch" virou detecção do utilitário instalado, nunca do próprio switch. |

## 13. Energia e CPU (lote proposto e parcialmente implementado em 26/07/2026, sétima rodada)

**Contexto que decide a maior parte desta seção**: o FiveMCleaner hoje é
uma ferramenta transacional de "aplicar uma vez, verificar, confirmar,
reverter se necessário" — o usuário clica em "Otimizar", o app aplica um
conjunto de ações e confirma. **Não existe hoje um processo de segundo
plano que observe o FiveM/GTA V iniciar e terminar** para aplicar algo "só
durante a sessão" e desfazê-lo automaticamente "ao fechar o jogo". A maior
parte deste lote (plano de energia próprio ativado só durante a sessão,
prioridade de processo testada e restaurada ao fechar, afinidade de CPU,
core parking, timer resolution solicitado enquanto o jogo está aberto)
**pressupõe exatamente esse tipo de vigilância de ciclo de vida**, que este
produto não tem.

Implementar esses itens "pela metade" — por exemplo, subir a prioridade do
processo do GTA uma vez, sem qualquer garantia de que ela será restaurada
quando o jogo fechar minutos ou horas depois, com o FiveMCleaner já
fechado — quebraria o princípio central de segurança deste projeto: toda
ação reversível tem que ter um caminho garantido de reversão. Por isso,
esta rodada **implementou o que já cabe no modelo atual (ajuste único,
reversível, sem depender de vigilância contínua)** e documentou o resto
como uma decisão de arquitetura pendente, não como algo "esquecido".

### Implementado nesta rodada

| Item | Classificação | Perfis | Observação |
| --- | --- | --- | --- |
| Ajustar PCI Express Link State Power Management | ✅ | Médio e Agressivo | **Implementado** — `windows.power.pcie-aspm.adjust`/`PciExpressPowerManagementAction`, via `powercfg /Q` + `/set{a,d}cvalueindex` no plano ativo (mesmo mecanismo documentado já usado por `SessionPerformancePowerPlanAction`). Totalmente reversível; se o computador não expõe essa configuração, ou a leitura do texto do `powercfg` não bate (varia por idioma do Windows), a ação simplesmente não faz nada, nunca falha. |

### Já coberto por infraestrutura existente, sem mudança de código

| Item | Observação |
| --- | --- |
| Não manter CPU em 100% permanentemente / Não usar Ultimate Performance como religião oficial | Já é a postura do produto: o plano de energia de alto desempenho só é ativado durante a otimização e é totalmente reversível; nunca fixado como "sempre ligado". |
| Nunca usar Realtime / Não elevar todos os processos indiscriminadamente / Não reduzir processos essenciais para Low | Já são regras implícitas do produto — nenhuma ação deste projeto jamais tocou prioridade de processo. |
| Não desativar CPU 0 / Não desativar SMT automaticamente / Não limitar o jogo a 4 núcleos | Já são regras implícitas — nenhuma ação deste projeto jamais tocou afinidade de CPU. |
| Não editar atributos ocultos do Registro permanentemente / Não desativar core parking sem medir | Já é a postura do produto para qualquer ajuste de registro (sempre reversível, nunca "permanente"). |
| Não instalar serviço permanente de timer / Não modificar HPET | Já é a postura do produto — nenhum serviço em segundo plano é instalado por este app. |
| Não tentar alterar software proprietário do mouse sem integração oficial | Já é a postura do produto (mesma razão de "não escrever perfil da NVIDIA/AMD/notebook" das seções 9–12). |
| Mouse polling: alertar/recomendar teste de 1000 Hz | **Implementado** — `windows.gaming.mouse-polling-rate.guide`/`MousePollingRateGuidanceAction`, condicionado à carga de CPU observada (não à taxa de polling real do mouse, que este app não consegue ler — ver limitação abaixo). |
| Mouse polling: detectar stutter coincidente com movimento do mouse | 🚫 **Não implementado** — exigiria telemetria contínua correlacionando eventos de mouse com frametime em tempo real; este produto só faz leituras pontuais (snapshot), não telemetria contínua. |

### Pendente de decisão de arquitetura (não implementado nesta rodada)

Todos os itens abaixo pressupõem um processo de vigilância de ciclo de
vida (detectar início/fim do FiveM/GTA V) que este produto não tem hoje.
Implementá-los exigiria antes uma decisão de arquitetura (ex.: um serviço
leve residente na bandeja, já existente para notificações, ganhar essa
responsabilidade; ou um processo separado) — decisão que deve ser tomada
explicitamente pelo usuário/mantenedor antes de qualquer código, não
decidida implicitamente ao implementar um item isolado.

| Item | Classificação pedida | Por que depende de vigilância de sessão |
| --- | --- | --- |
| Criar plano `FiveMCleaner Gaming` (duplicar, ativar só na sessão, restaurar ao fechar) | ✅ | "Ativar apenas durante a sessão" e "restaurar ao fechar FiveM" exigem saber quando o FiveM abre/fecha. `powercfg` já suporta duplicar/criar/importar planos — a parte tecnicamente viável (`powercfg -duplicatescheme`) não é o problema; o problema é o gatilho de início/fim. |
| Configurar comportamento diferente para desktop/notebook | ✅ | Viável tecnicamente (já detectamos bateria/notebook na seção 12), mas só faz sentido dentro do plano de sessão acima. |
| Não permitir economia agressiva de CPU / não desligar disco durante o jogo | ✅ | Mesma dependência — são configurações do plano de sessão proposto. |
| Configurar refrigeração ativa em notebooks | 🟡 | Depende do utilitário do fabricante (seção 12) — mesma limitação de API, mesma dependência de sessão. |
| Ajustar estado mínimo do processador / modo de boost | 🟡 | Tecnicamente são configurações de `powercfg` (viável), mas "só durante a sessão" tem a mesma dependência. |
| Detectar processo real do GTA dentro do FiveM | ✅ | Tecnicamente simples (o app já tem `IFiveMProcessInspector`/`IGtaVProcessInspector`); o que falta é o gatilho de sessão para as ações que dependem dessa detecção continuamente. |
| Prioridade `Above Normal`/teste `High`, restaurar ao fechar | 🟡/🧪 | Exige monitorar o processo até ele fechar para restaurar — sem isso, "restaurar ao fechar" não pode ser garantido. |
| Afinidade de CPU (P-cores/E-cores, CCDs, restaurar ao terminar) | 🧪 | Mesma dependência; também exige detectar a topologia real da CPU (híbrida Intel, múltiplos CCDs AMD) de forma confiável, pesquisa ainda não feita. |
| Core parking (alterar só dentro do plano, restaurar ao fechar) | 🧪 | Mesma dependência de sessão. |
| Timer resolution (solicitar enquanto o jogo está aberto, liberar ao fechar, medir latência/consumo) | 🧪 | Mesma dependência de sessão; medir "latência e consumo" também exigiria a infraestrutura de benchmark antes/depois ainda não construída para esse propósito. |

**Recomendação para uma futura sessão**: antes de implementar qualquer um
destes, decidir e documentar (em `docs/architecture.md`) como o app vai
detectar o ciclo de vida do FiveM/GTA V em tempo real e garantir reversão
mesmo se o FiveMCleaner for fechado antes do jogo. Só depois faz sentido
portar os itens ✅/🟡/🧪 desta tabela para o catálogo.

## Resumo por perfil

- **Leve**: nada novo entra aqui além do que já existe hoje (GPU de alto desempenho, Modo de Jogo) — este lote não adiciona itens automáticos ao perfil Leve, mantendo-o o mais conservador.
- **Médio**: ganha a troca de frequência do monitor com confirmação (🟡) além do que já existe.
- **Agressivo**: ganha todos os itens 🧪 (janela sem bordas Win11, Fullscreen Optimizations por app, HAGS, Modo de Jogo desligado condicional) e a troca de frequência do monitor.
- **Diagnóstico (👁, todos os perfis, sem alterar nada)**: detecção de GPU integrada por engano, suporte/estado de VRR, cabo/porta inadequados, frequência do monitor abaixo do máximo.
- **Manual, fora de qualquer perfil automático**: HDR/Auto HDR (🟡 ativar por preferência, 🔧 desativar em caso de problema); reinstalação limpa de driver guiada (🔧, sob demanda).
- **Pendente de pesquisa antes de qualquer código**: habilitar VRR do Windows programaticamente — só vira ✅/🟡 codificável depois de confirmar um mecanismo público e testá-lo em hardware real.
- **🚫 Não implementar (lote NVIDIA, terceira rodada)**: praticamente toda configuração de perfil 3D por aplicativo do driver NVIDIA (baixa latência, limite de FPS pelo driver, G-SYNC por aplicativo, Shader Cache Size, Texture Filtering Quality, Threaded Optimization, NVIDIA Image Scaling, DSR, gerenciamento de energia por app, criar perfil por aplicativo) — não existe API pública e oficialmente suportada da NVIDIA para escrever essas configurações; a política já registrada em `docs/safety.md` ("ajustes de perfil 3D só pelo painel oficial do fabricante") já cobria isso antes mesmo desta lista chegar. Overclock, voltagem e power limit continuam fora de escopo por definição do produto, sem exceção.
- **Diagnóstico implementado na quarta rodada**: o diagnóstico de drivers alerta, por data WMI, quando o driver de vídeo tem mais de 18 meses; o detector de overlays reconhece NVIDIA Share/ShadowPlay e orienta conferir Instant Replay/Freestyle; a ação `windows.gaming.gsync.guide` orienta G-SYNC/FreeSync/VRR e limite de FPS sem alterar o driver. Freestyle isolado e o estado real de VRR continuam sem sinal público confiável e não são apresentados como fatos.

## Próximos passos

1. As rodadas quarta e quinta implementaram apenas o subconjunto seguro de diagnóstico/orientação das seções 9–11: G-SYNC/FreeSync/VRR, driver antigo, NVIDIA Share/ShadowPlay e reinstalação guiada. Escritas no perfil do driver continuam deliberadamente não implementadas por falta de API pública suportada; ver `PROJECT_STATE.md` para o estado integrado.
2. Antes de implementar qualquer item 🧪, revisar `docs/safety.md` para garantir que o padrão de comparação antes/depois e reversão automática siga o mesmo modelo já usado por `OptimizationComparisonResult`/`ComputeRegressionReasonKeys`.
3. A pesquisa sobre o mecanismo de habilitar VRR via Windows precisa ser registrada em `docs/research.md` (Fato/Inferência/Fora de escopo) antes de qualquer código ser escrito para esse item específico.
4. Qualquer evolução que pretenda distinguir Freestyle isolado, confirmar o estado real de VRR ou escrever configurações do driver depende primeiro de uma API pública suportada e de pesquisa registrada; até lá, manter orientação best-effort e somente leitura.
