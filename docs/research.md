# Base de pesquisa

Esta página registra as evidências usadas para definir o escopo e as políticas do FiveMCleaner. A revisão atual foi fechada em **18 de julho de 2026**; itens dependentes de versão precisam ser revalidados antes de cada release.

## Como ler

- **Fato**: comportamento documentado por fonte oficial, suporte do Cfx.re ou código-fonte oficial do FiveM Legacy.
- **Inferência**: decisão prudente derivada desses fatos, ainda sujeita a benchmark e validação em hardware real.
- **Fora de escopo**: comportamento que não deve ser automatizado pelo produto atual.

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

Esses resultados são antigos e dependentes de hardware. Por isso, os presets do FiveMCleaner são hipóteses conservadoras e devem ser medidos em hardware real antes de receber alegações quantitativas.

## Antivírus e integridade

**Fato.** O Cfx.re documenta conflitos possíveis entre antivírus e anti-cheat, locks em `server-cache-priv` e encerramento por anomalias de integridade.

- [Lock em `server-cache-priv`](https://support.cfx.re/hc/en-us/articles/8039663675036-Opening-database-privcache-failed-IO-error-Could-not-lock-file-error-when-I-try-to-launch-FiveM)
- [KERNELBASE e conflito com antivírus](https://support.cfx.re/hc/en-us/articles/5299951678748-FiveM-crashing-with-KERNELBASE-dll-RaiseException-error)
- [Game integrity check failed](https://support.cfx.re/hc/en-us/articles/12505932916508-Game-integrity-check-failed-error-in-FiveM)

**Inferência.** Para reduzir falsos positivos, o FiveMCleaner não deve injetar DLL, alterar memória de processos, patchar executáveis, ofuscar payloads, baixar scripts executáveis ou desativar ferramentas de segurança. Não existe garantia honesta de detecção zero em todos os antivírus.

## Marca e representação

O acordo do Cfx.re proíbe representação que sugira endosso ou afiliação. A comunicação pública deve apresentar o FiveMCleaner como projeto independente “para FiveM”, incluir disclaimer claro e evitar o logo oficial como marca própria.

Fonte: [Cfx.re Platform Service Agreement](https://runtime.fivem.net/fivem-service-agreement-4.pdf), seção “Representation”.
