# Base de pesquisa

Esta página registra as evidências usadas para definir o escopo e as políticas do Ralven. A revisão atual foi fechada em **30 de agosto de 2026**; itens dependentes de versão precisam ser revalidados antes de cada release.

## Como ler

- **Fato**: comportamento documentado por fonte oficial, suporte do Cfx.re ou código-fonte oficial do FiveM Legacy.
- **Inferência**: decisão prudente derivada desses fatos, ainda sujeita a benchmark e validação em hardware real.
- **Fora de escopo**: comportamento que não deve ser automatizado pelo produto atual.

## Windows geral

Esta expansão distingue diagnóstico suportado de automação baseada em chaves
privadas ou heurísticas de internet. O escopo geral usa somente capacidades já
tipadas no Ralven e não exige FiveM/GTA instalado.

### Energia

**Fato.** O Windows expõe o esquema ativo pelas APIs
`PowerGetActiveScheme`/`PowerSetActiveScheme` e documenta `powercfg /getactivescheme`,
`/list` e `/setactive`. O controle pode ser recusado pela ACL de energia da
máquina e não exige inventar um plano “Ultimate” com dezenas de índices ocultos.

Fontes:

- [Managing Power Schemes](https://learn.microsoft.com/en-us/windows/win32/power/managing-power-schemes)
- [PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme)
- [Opções do powercfg](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)

**Decisão.** O Ralven pode ativar um esquema de desempenho somente após ler o
GUID atual, verificar alimentação por tomada e guardar o estado anterior para
rollback. Acesso negado pode acionar o broker tipado; esquema inexistente vira
`Skipped`, não uma criação improvisada.

**Fato sobre ASPM.** O Windows documenta a configuração PCI Express Link State
Power Management pelo GUID `ee12f906-d277-404b-b6da-e5fa1a576df5`, com índices
0 (Off), 1 (economia moderada) e 2 (economia máxima). As APIs
`PowerReadACValueIndex` e `PowerReadDCValueIndex` leem separadamente os valores
na tomada e na bateria; `powercfg /setacvalueindex`, `/setdcvalueindex` e
`/setactive` são os mecanismos oficiais de gravação e ativação. A documentação
prova o mecanismo de energia, não um ganho universal de FPS ou latência.

Fontes:

- [Link State Power Management](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/pci-express-settings-link-state-power-management)
- [PowerReadACValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex)
- [PowerReadDCValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreaddcvalueindex)
- [Opções do powercfg](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)

**Decisão sobre ASPM.** O Ralven só aplica Off quando ambos os índices podem ser
lidos. Ele captura AC e DC separadamente, compensa qualquer falha parcial,
reativa o plano, relê a pós-condição e recusa rollback sobre uma escolha mais
nova. Hardware sem a configuração vira `Skipped`. O texto público descreve o
efeito como condicional e explicita consumo/temperatura maiores.

### Tela e taxa de atualização

**Fato.** `EnumDisplaySettings` expõe o modo atual e os modos disponíveis por
display. A própria documentação/suporte do Windows trata taxa dinâmica (DRR) e
taxa fixa como escolhas contextuais, especialmente em notebooks.

Fontes:

- [EnumDisplaySettings](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsa)
- [Alterar a taxa de atualização no Windows](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-the-refresh-rate-on-your-monitor-in-windows)

**Decisão.** Comparar a taxa atual com a maior taxa da mesma resolução é um
diagnóstico útil. O plano geral não troca modo, resolução ou frequência: uma
alteração automática pode piorar bateria, selecionar combinação incompatível ou
exigir reinício. A superfície nativa de Display Avançado continua sendo o local
seguro para a escolha do usuário.

### Inicialização, pagefile, memória e armazenamento

**Fato.** `Run`/`RunOnce` e as pastas Startup são locais documentados de
inicialização, mas o Windows não fornece uma API pública geral equivalente ao
botão de desabilitar do Gerenciador de Tarefas para itens arbitrários.
`Win32_PageFileSetting` descreve configuração persistente de pagefile, que exige
privilégio e pode só entrar em vigor no próximo boot. `fsutil` documenta o estado
de delete notification/TRIM, e `Optimize-Volume -ReTrim` é uma manutenção de
volume, não uma forma universal de “acelerar SSD”.

Fontes:

- [Run e RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [Startup apps](https://learn.microsoft.com/en-us/windows/win32/w8cookbook/startup-apps)
- [Win32_PageFileSetting](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pagefilesetting)
- [fsutil behavior](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil-behavior)
- [Optimize-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/optimize-volume?view=windowsserver2025-ps)

**Decisão.** Nesta etapa, inicialização, pagefile e memória permanecem somente
diagnóstico; o Ralven não escreve `StartupApproved`, não dimensiona pagefile por
heurística de RAM e não implementa “RAM cleaner”. O plano pode consultar a
política numérica de TRIM sem alteração; mudar a política ou executar ReTrim só
poderá virar ação futura após existir detecção de filesystem/volume, privilégio
tipado, verificação e uma apresentação explícita de que ReTrim não possui
rollback.

## Centro de aplicativos inspirado no UniGetUI

**Fato.** O UniGetUI oficial organiza sua experiência em catálogos de pacotes
disponíveis, instalados, atualizáveis e bundles. Gerenciadores implementam um
contrato comum com capacidades próprias; loaders preenchem as listas, enquanto
a camada de operações mantém fila e histórico. A interface oferece busca,
filtros, operações em lote, versões ignoradas, atualização automática,
agendamento, fontes, importação/exportação e opções por pacote. No Windows, o
projeto suporta atualmente WinGet, Scoop, Chocolatey, PowerShell, npm, Bun,
pip, Cargo, .NET Tool e vcpkg.

Fontes:

- [Repositório e recursos oficiais do UniGetUI](https://github.com/Devolutions/UniGetUI)
- [Arquitetura oficial do UniGetUI](https://github.com/Devolutions/UniGetUI/blob/main/AGENTS.md)
- [Referência da CLI do UniGetUI](https://github.com/Devolutions/UniGetUI/blob/main/docs/CLI.md)

**Decisão personalizada.** O Ralven replica a base de navegação e operação que
é útil ao seu propósito — Descobrir, Atualizações, Gerenciados, seleção em lote
e lista persistente de ignorados — sem copiar a amplitude ou os riscos de um
gerenciador universal. A primeira base usa um único executor nativo, WinGet,
para as origens padrão confiáveis `winget` e `msstore`. Scoop exigiria executar
scripts PowerShell do usuário; Chocolatey normalmente executa scripts de pacote
com elevação. Nenhum deles será encaixado no broker como comando ou rede
genéricos. Atualização automática, agendador, fontes arbitrárias, bundles e
importação/exportação também ficam de fora até terem política de consentimento,
proveniência, recuperação e testes próprios.

## Operações de aplicativos pelo WinGet

**Fato.** O Windows Package Manager oferece os comandos `list` e `upgrade` no
Windows 10 e 11. `list --upgrade-available` limita o resultado aos aplicativos
com atualização aplicável, e `upgrade --id ... --exact --source winget` limita a
execução a uma identidade e fonte específicas. O WinGet mantém verificações de
hash e não reinicia o computador sem `--allow-reboot`.

Fontes:

- [Comando list do WinGet](https://learn.microsoft.com/windows/package-manager/winget/list)
- [Comando upgrade do WinGet](https://learn.microsoft.com/windows/package-manager/winget/upgrade)
- [Códigos de retorno do WinGet](https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md)

As origens padrão documentadas incluem `winget` e `msstore`. Busca e listagem
usam `search`/`list` com origem fixa e sem aceitar termos automaticamente.
Instalação, atualização e desinstalação usam `--id`, `--exact` e `--source`; as
duas primeiras aceitam termos somente após a confirmação explícita do Ralven.
As operações não usam `--force`, não ignoram hash e não autorizam reinício. O
Ralven não tenta reconstruir rollback de um instalador de terceiros; ele informa
essa limitação antes da execução e reporta o código de retorno sem mascarar
falhas.

Fonte adicional:

- [Gerenciamento de origens do WinGet](https://learn.microsoft.com/windows/package-manager/winget/source)
- [Comando install do WinGet](https://learn.microsoft.com/windows/package-manager/winget/install)

### Proteções do Windows e aceleração do ponteiro

**Fato.** `WscGetSecurityProviderHealth` retorna a saúde agregada da categoria
de proteção solicitada. `SystemParametersInfo` com `SPI_GETMOUSE` retorna os dois
limiares e o nível de aceleração do ponteiro em um vetor de três inteiros;
`SPI_SETMOUSE` grava esses mesmos três valores. `SPIF_UPDATEINIFILE` persiste a
mudança e `SPIF_SENDCHANGE` notifica os aplicativos.

Fontes:

- [WscGetSecurityProviderHealth](https://learn.microsoft.com/en-us/windows/win32/api/wscapi/nf-wscapi-wscgetsecurityproviderhealth)
- [SystemParametersInfo e SPI_GETMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)

**Decisão.** O plano geral consulta as três categorias de proteção separadamente
e não interpreta falha da Central de Segurança como estado saudável. A leitura
do mouse continua disponível como diagnóstico. O Ultra pode, por preferência
explícita, zerar limiares e aceleração sem alterar a velocidade do ponteiro;
captura os três valores anteriores, verifica aplicação e rollback e não deduz o
caminho de entrada usado por um jogo a partir dessa configuração.

### Responsividade da interface

**Fato.** `SystemParametersInfo` expõe contratos públicos para consultar e
alterar animações da interface e `SPI_GETMENUSHOWDELAY`/
`SPI_SETMENUSHOWDELAY` representam, em milissegundos, o tempo que o Windows
aguarda antes de abrir um menu em cascata.

Fonte:

- [SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)

**Decisão.** Os perfis Médio e Agressivo podem limitar o atraso dos menus a
100 ms, sem aumentar um valor menor já escolhido pelo usuário. O perfil
Agressivo também pode reduzir animações allowlisted. As duas ações releem suas
pós-condições e guardam os valores anteriores para rollback; suavização de
fontes e parâmetros de acessibilidade não relacionados permanecem intactos.

### HAGS, VRR e perfis de fabricante

**Fato.** A Microsoft documenta a superfície de Configurações e a capacidade
DXGI `DXGI_FEATURE_PRESENT_ALLOW_TEARING`, mas essa capacidade não prova que
VRR está ativo no monitor e não existe contrato público geral para editar HAGS
ou perfis 3D NVIDIA/AMD de terceiros.

Fonte: [Variable refresh rate displays](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/variable-refresh-rate-displays).

**Decisão.** O escopo geral pode relatar fatos comprováveis e abrir a superfície
nativa, mas não promove HAGS, VRR, G-SYNC/FreeSync ou perfil de fabricante a
ajuste automático. A existência de uma chave observada em builds atuais não a
transforma em API suportada.

## Estado das edições

**Fato.** O FiveM para GTAV Enhanced foi anunciado para early access em **21 de julho de 2026**, por meio de um launcher separado. O FiveM Legacy seguirá disponível em paralelo. O instalador Enhanced permitirá escolher outro local para o cache, e o cliente removerá automaticamente recursos que servidores deixaram de oferecer.

Fontes:

- [Development Update #2: FiveM for GTAV Enhanced](https://forum.cfx.re/t/development-update-2-fivem-for-gtav-enhanced/5412576)
- [Development Update #3: FiveM for GTAV Enhanced](https://forum.cfx.re/t/development-update-3-fivem-for-gtav-enhanced/5415045)

**Inferência.** Caminhos, formato de configurações e regras de cache do Legacy não podem ser transferidos ao Enhanced. A primeira versão deve identificar a edição e retornar um bloqueio seguro para Enhanced.

## Requisitos conhecidos

O diagnóstico não usa o requisito mínimo como promessa de boa experiência. Ele serve para contextualizar limites que um perfil de software não consegue remover.

| Edição            | Mínimo relevante                                                                                                   | Recomendado relevante                                                                                    |
| ----------------- | ------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------- |
| FiveM/GTAV Legacy | Windows 10 x64; CPU Q6600/Phenom 9850; GPU 1 GB; FiveM recomenda 8 GB de RAM; aproximadamente 4 GB extras de cache | i5-3470/FX-8350; GTX 660/HD 7870; FiveM recomenda 16 GB de RAM; aproximadamente 10 GB extras de cache    |
| GTAV Enhanced     | Windows 10 build 1909; i7-4770/FX-9590; GPU de 4 GB; 8 GB RAM; SSD obrigatório                                     | Windows 11; i5-9600K/Ryzen 5 3600; GPU de 8 GB; 16 GB dual-channel; unidade compatível com DirectStorage |

Fontes:

- [Requisitos no site do FiveM](https://fivem.net/en/)
- [Requisitos do FiveM no suporte Cfx.re](https://support.cfx.re/hc/en-us/articles/8017221737244-FiveM-system-requirements)
- [Requisitos de GTAV Legacy e Enhanced no suporte Rockstar](https://support.rockstargames.com/articles/lMQXeP2Z1mN3g9oZiBZFR/grand-theft-auto-v-pc-system-requirements)

Há páginas antigas do Cfx.re que ainda mencionam Windows 8.1. O gate conservador do produto usa Windows 10 x64, alinhado ao requisito atual do GTAV.

## Instalação e dados do Legacy

**Fato.** Quando instalado pelo fluxo padrão, o FiveM fica em `%LOCALAPPDATA%\FiveM`; executar o instalador em uma pasta vazia pode criar uma instalação personalizada. Portanto, descoberta por caminho conhecido precisa ter fallback para localização personalizada.

Fonte: [Installing FiveM](https://docs.fivem.net/docs/client-manual/installing-fivem/).

O código oficial registra a migração dos caches antigos para a estrutura atual:

| Caminho relativo a `FiveM.app` | Papel observado                              | Política derivada                            |
| ------------------------------ | -------------------------------------------- | -------------------------------------------- |
| `data\server-cache`            | índice e pacotes de recursos                 | regenerável; manutenção sob demanda          |
| `data\server-cache-priv`       | conteúdo privado baixado de servidores       | regenerável; limpeza invalida alguns replays |
| `data\game-storage`            | arquivos e builds locais requeridos          | protegido; nunca em limpeza automática       |
| `data\nui-storage`             | cache/armazenamento do Chromium/NUI          | protegido; reparo explícito somente          |
| `data\cache`                   | metadados internos do launcher e diagnóstico | não remover em bloco                         |
| `data\ipfs`                    | armazenamento interno migrado                | fora da limpeza genérica                     |

Fontes:

- [Mapeamento atual de cache no código oficial](https://github.com/citizenfx/fivem/blob/master/code/client/launcher/ViabilityChecks.cpp)
- [`game-storage` e verificação de arquivos](https://github.com/citizenfx/fivem/blob/master/code/client/launcher/GameCache.cpp)
- [`server-cache` usado pelo mounter de recursos](https://github.com/citizenfx/fivem/blob/master/code/components/citizen-resources-client/src/CachedResourceMounter.cpp)
- [Explicação de `server-cache-priv` e impacto no Rockstar Editor](https://forum.cfx.re/t/when-i-join-a-server-how-do-i-find-out-where-those-mods-installed/4847248)

**Fato.** A documentação oficial não apresenta limpeza periódica de cache como técnica para elevar FPS. No Enhanced, o próprio Cfx.re descreve o problema do Legacy como consumo acumulado de disco.

**Inferência.** Limpar cache pode recuperar espaço ou corrigir corrupção, mas provoca novo download e pode piorar temporariamente a primeira conexão. Por isso, cache não compõe automaticamente os perfis Leve, Médio ou Agressivo.

## Dados que não são cache

| Item                                               | Função                                            | Conduta                                                 |
| -------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------- |
| `FiveM.app\CitizenFX.ini`                          | caminho do GTAV, build, canal e opções do cliente | backup e edição apenas de propriedades documentadas     |
| `%APPDATA%\CitizenFX\gta5_settings.xml`            | configurações gráficas do FiveM Legacy            | principal alvo dos perfis gráficos; edição transacional |
| `%APPDATA%\CitizenFX\fivem.cfg`                    | convars persistentes e binds                      | preservar                                               |
| `Documents\Rockstar Games\GTA V\...\fivem_set.bin` | perfil do FiveM                                   | preservar                                               |
| `FiveM.app\plugins`                                | plugins escolhidos pelo usuário                   | inventariar; nunca apagar silenciosamente               |
| `%APPDATA%\CitizenFX\ros_id.dat`                   | estado de autenticação                            | remover somente no reparo exato documentado             |
| `%LOCALAPPDATA%\DigitalEntitlements`               | entitlement Rockstar                              | remover somente no reparo exato documentado             |

O código do FiveM direciona `gta5_settings.xml` para `fxd:/`, e `fxd:/` é montado em `%APPDATA%\CitizenFX`:

- [VidBehavior.cpp](https://github.com/citizenfx/fivem/blob/master/code/components/gta-core-five/src/VidBehavior.cpp)
- [CitizenMount.Shared.cpp](https://github.com/citizenfx/fivem/blob/master/code/components/rage-device-five/src/CitizenMount.Shared.cpp)

Comandos persistidos com `seta` são documentados em `%APPDATA%\CitizenFX\fivem.cfg`: [Console commands](https://docs.fivem.net/docs/client-manual/console-commands/).

## Configurações oficialmente expostas

O `CitizenFX.ini` documenta:

- `IVPath`;
- `SavedBuildNumber`;
- `UpdateChannel=production|beta|canary`;
- `DisableNVSP`;
- `EnableFullMemoryDump`;
- `DisableOSVersionCheck`;
- `DisableCrashUpload`.

Fonte: [CitizenFX.ini](https://docs.fivem.net/docs/client-manual/citizenfx/).

Decisões:

- `production` é o canal esperado; beta e canary são oficialmente instáveis;
- `SavedBuildNumber` pode evitar transição de build, mas não é ganho de FPS;
- o app não desabilita verificação de versão do Windows;
- upload de crash é preferência de privacidade, não otimização;
- full dumps podem ocupar 1–10 GB e só devem ser removidos depois de preservado o diagnóstico necessário;
- o overlay GeForce não deve ser habilitado automaticamente, pois o Cfx.re registra problemas de crash.

Os argumentos de atalho documentados são `-cl2`, `-pure_X` e `-bXXXX`. Nenhum é apresentado como otimização de FPS: [FiveM Shortcut](https://docs.fivem.net/docs/client-manual/shortcut/).

## Tweaks populares que não se aplicam

**Fato.** O FiveM Legacy bloqueia explicitamente a leitura do `commandline.txt` do GTA. Logo, escrever `-high`, `-disableHyperthreading`, `-useMinimumSettings` ou opções gráficas nesse arquivo não implementa um perfil válido para FiveM.

Fonte: [BlockLoadSetters.cpp](https://github.com/citizenfx/fivem/blob/master/code/components/gta-core-five/src/BlockLoadSetters.cpp#L528-L540).

**Fato.** No caminho NVIDIA, o FiveM cria um perfil por executável, direciona notebooks à dGPU e desliga o shader disk cache por uma race condition de driver.

Fonte: [NvCacheWorkaround.cpp](https://github.com/citizenfx/fivem/blob/master/code/client/citigame/NvCacheWorkaround.cpp#L130-L145).

**Inferência.** O produto não deve sobrescrever perfis NVIDIA, forçar shader cache, definir afinidade fixa, prioridade `Realtime` ou desativar SMT. Além de não haver evidência universal de benefício, essas ações podem contrariar proteções do próprio cliente.

## Diagnóstico de desempenho

Comandos oficiais úteis:

- `cl_drawfps true`: contador de FPS;
- `cl_drawperf true`: FPS, ping, perda de pacotes, uso de CPU/GPU e temperatura da GPU;
- `netgraph true`: comportamento de rede em tempo real;
- `net_statsFile`: captura de métricas de rede;
- `resmon true`: CPU e memória por recurso, quando o modo de desenvolvimento está disponível.

Fonte: [Console commands](https://docs.fivem.net/docs/client-manual/console-commands/).

**Fato.** O suporte Cfx.re recomenda testar outro servidor quando o problema pode ser específico daquele servidor. Para quedas persistentes em hardware adequado, a orientação oficial é capturar um ETW trace.

- [Client issues](https://docs.fivem.net/docs/support/client-issues/)
- [ETW trace para troubleshooting](https://support.cfx.re/hc/en-us/articles/8366604193436-Creating-an-Event-Tracing-for-Windows-ETW-trace-log-for-FiveM-Troubleshooting)

**Inferência.** Um diagnóstico deve distinguir:

- GPU saturada: reduzir resolução, AA, grama e efeitos tende a ser mais útil;
- CPU ou recurso de servidor: diminuir resolução pode não ajudar;
- VRAM pressionada: reduzir textura e distância, sem ultrapassar limites sugeridos;
- rede: ping e packet loss não são FPS;
- disco: pouco espaço e downloads de cache afetam carregamento, não necessariamente FPS sustentado;
- temperatura: throttling não é resolvido por limpeza de cache;
- servidor específico: encaminhar evidências aos responsáveis pelo servidor.

## Fundamento dos perfis gráficos

O projeto usa o `gta5_settings.xml` existente e preserva o schema encontrado. Não distribui um XML universal.

Como referência secundária de custo visual, o guia da NVIDIA para GTAV identifica MSAA, grama e distância estendida como ajustes de impacto relevante; textura afeta sobretudo VRAM, e ultrapassar o limite sugerido pode causar stutter e crashes.

Fonte: [Grand Theft Auto V PC Graphics & Performance Guide](https://www.nvidia.com/en-us/geforce/news/grand-theft-auto-v-pc-graphics-and-performance-guide/).

Esses resultados são antigos e dependentes de hardware. Por isso, os presets do Ralven são hipóteses conservadoras e devem ser medidos em hardware real antes de receber alegações quantitativas.

## Antivírus e integridade

**Fato.** O Cfx.re documenta conflitos possíveis entre antivírus e anti-cheat, locks em `server-cache-priv` e encerramento por anomalias de integridade.

- [Lock em `server-cache-priv`](https://support.cfx.re/hc/en-us/articles/8039663675036-Opening-database-privcache-failed-IO-error-Could-not-lock-file-error-when-I-try-to-launch-FiveM)
- [KERNELBASE e conflito com antivírus](https://support.cfx.re/hc/en-us/articles/5299951678748-FiveM-crashing-with-KERNELBASE-dll-RaiseException-error)
- [Game integrity check failed](https://support.cfx.re/hc/en-us/articles/12505932916508-Game-integrity-check-failed-error-in-FiveM)

**Inferência.** Para reduzir falsos positivos, o Ralven não deve injetar DLL, alterar memória de processos, patchar executáveis, ofuscar payloads, baixar scripts executáveis ou desativar ferramentas de segurança. Não existe garantia honesta de detecção zero em todos os antivírus.

## Marca e representação

O acordo do Cfx.re proíbe representação que sugira endosso ou afiliação. A comunicação pública deve apresentar o Ralven como projeto independente “para FiveM”, incluir disclaimer claro e evitar o logo oficial como marca própria.

Fonte: [Cfx.re Platform Service Agreement](https://runtime.fivem.net/fivem-service-agreement-4.pdf), seção “Representation”.
