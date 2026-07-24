# Estado do Projeto

## Visão geral e objetivo

FiveMCleaner é um aplicativo desktop para Windows voltado à otimização
transparente, reversível e orientada por diagnóstico do FiveM para GTAV Legacy.
Ele prioriza mudanças pequenas, verificáveis e com rollback, sem prometer ganho
universal de FPS nem comprometer proteções do sistema.

O checkout de desenvolvimento canônico fica em `C:\Projetos\FiveMCleaner`.
O atalho de área de trabalho **FiveMCleaner - Desenvolvimento** usa o launcher
versionado em `scripts\Start-DevelopmentApp.ps1`: a cada abertura ele recompila
o build Release atual e inicia o executável normal, sem argumentos de simulação.
Isso permite acompanhar as mudanças locais sem manter uma cópia congelada do app.

## Tecnologias

- C# / .NET SDK 10.0.302, definido em `global.json`;
- WPF para a aplicação desktop (`net10.0-windows10.0.19041.0`);
- xUnit para testes automatizados;
- PowerShell para automação de validação, pacote portável e instalador;
- Inno Setup para o instalador Windows self-contained;
- Next.js, React, TypeScript, Vite/Vinext e ESLint para o site em `website/`;
- GitHub Actions para CI e para o workflow manual de release.

## Interface e preferências

- A interface usa Segoe UI Variable, cards com espaçamento consistente e um
  painel de hardware em duas colunas que preserva nomes completos de CPU e de
  todas as GPUs detectadas. O diagnóstico também mostra Windows e arquitetura.
- Configurações gerais possuem somente idioma, tema, comportamento do X e
  inicialização com Windows. O fechamento é uma escolha explícita entre sair e
  manter o app na área de notificação; a bandeja usa o ícone oficial e menu
  localizado. O tema Sistema acompanha as notificações de preferência do
  Windows quando o sistema as fornece.
- A pontuação de prontidão é um sinal de capacidade, não uma estimativa de FPS:
  a tela explica os fatores observados (memória, processadores lógicos, disco,
  GPU, cache e edição do FiveM).

## Arquitetura

A solução `FiveMCleaner.slnx` separa responsabilidades em projetos:

- `FiveMCleaner.App`: interface WPF, tema, localização, diagnósticos exibidos,
  progresso, preferências e interação do usuário; não deve executar operações
  administrativas diretamente.
- `FiveMCleaner.Contracts`: DTOs, identificadores, enums e contratos tipados
  compartilhados entre processos.
- `FiveMCleaner.Core`: catálogo de ações, composição dos perfis e planejamento
  de otimização; não depende de WPF, registro ou sistema de arquivos.
- `FiveMCleaner.Windows`: descoberta de FiveM/GTA e adaptadores Windows para
  ações permitidas, filesystem, registro e transações.
- `FiveMCleaner.Broker`: processo administrativo efêmero, com allowlist e
  contratos validados; não aceita shell, scripts nem comandos arbitrários.
- `FiveMCleaner.Tests`: testes de contratos, planejamento, ações Windows,
  rollback e serviços da aplicação.

O fluxo central é: diagnóstico factual, criação de plano imutável, prévia e
consentimento, snapshot, execução por ação, validação, journal local e rollback
quando aplicável. O broker recebe apenas o subconjunto administrativo já
aprovado e tipado.

## Estrutura relevante

```text
src/                         Aplicação e camadas .NET
tests/FiveMCleaner.Tests/    Testes xUnit
scripts/                     Validações, pacote e instalador
installer/                   Script Inno Setup e contrato de release
docs/                        Arquitetura, segurança, pesquisa e distribuição
.github/workflows/           CI e release manual
website/                     Landing page Next/React independente
artifacts/, publish/, tmp/   Saídas locais ignoradas pelo Git
```

## Decisões técnicas e padrões

- O produto atende apenas FiveM para GTAV Legacy. GTAV Enhanced deve ser
  detectado e bloqueado com segurança até existir um adaptador específico.
- Cada ação de sistema precisa ter escopo conhecido, pré-condições,
  pós-validação, resultado tipado e estratégia de rollback quando possível.
- Perfis Leve, Médio e Agressivo são composições de ações versionadas; eles não
  executam operações diretamente. O usuário nunca vê nem marca uma lista de
  tweaks individuais — apenas escolhe o modo.
- A execução do fluxo padrão do app é isolada por ação (verificar → aplicar →
  validar → registrar, uma falha reverte só a própria ação); falhas críticas
  abortam o restante com segurança e nenhum sucesso parcial é relatado como
  total. Ver `docs/safety.md` (seção "Execução isolada por ação") e
  `docs/architecture.md`. O catálogo de ações está na versão 7
  (`ActionCatalog.CurrentVersion`).
- Avaliação da "PRIMEIRA FASE" de adições pedida pelo usuário em 23/07/2026:
  backup/restauração, presets do `settings.xml`, gerenciamento de cache,
  plano de energia temporário e GPU de alto desempenho já estavam
  implementados; gargalo, overlays/captura e leitura de log foram adicionados
  como diagnósticos somente leitura (acima). Dois itens ficaram
  deliberadamente de fora com a concordância do usuário: um "modo sessão" que
  fecha/reabre aplicativos automaticamente (risco real de perda de dados em
  apps com trabalho não salvo, e reabrir não restaura o estado interno) e um
  benchmark de FPS embutido (exigiria overlay/hook, proibido pelo modelo de
  segurança). Se o benchmark ou o modo sessão forem retomados, tratar como
  nova decisão de produto com o usuário, não como extensão automática do
  catálogo — ver `docs/safety.md` para as proibições exatas.
- Caches e arquivos sensíveis são tratados por allowlist e condições
  explícitas. Dados de autenticação, `game-storage`, NUI storage,
  configurações e plugins não são lixo automático.
- A interface é localizada para pt-BR e inglês, com tema claro, escuro ou do
  sistema. Identificadores de código permanecem em inglês.
- Preferências, journals, solicitações efêmeras e logs locais ficam sob
  `%LOCALAPPDATA%\FiveMCleaner`; não devem ser gravados dentro da pasta de
  instalação.
- O instalador publica o runtime .NET junto ao aplicativo (`win-x64`
  self-contained). O atualizador consulta apenas releases estáveis públicas do
  GitHub, valida versão, origem HTTPS e SHA-256, e só abre o instalador após
  confirmação explícita do usuário.
- O formulário de bugs é opt-in: nenhum dado é enviado sem o clique do usuário.
  Imagens opcionais passam por sanitização antes do envio.
- A telemetria técnica também é estritamente opt-in e fica desativada por
  padrão. Após consentimento explícito, só envia por HTTPS uma allowlist de
  tipo de término da otimização, duração, versão e categoria fixa de erro; não
  envia logs, texto livre, caminhos, arquivos, documentos, histórico, hardware
  ou dados pessoais. O contrato e a ressalva sobre metadados de transporte do
  FormSubmit estão em `docs/telemetry.md`; falhas de telemetria nunca interferem
  na otimização nem geram novas tentativas automáticas.
- A licença do código próprio é a `FiveMCleaner Source-Available License v1.0`
  em `LICENSE`, substituindo a MIT. Ela permite estudo, auditoria, compilação
  para uso próprio, forks de desenvolvimento e Pull Requests, mas restringe
  venda, redistribuição de executáveis modificados, remoção de créditos, uso da
  marca e produtos concorrentes derivados. Licenças de dependências de terceiros
  continuam válidas e independentes.

## Funcionalidades presentes no código atual

- Diagnóstico de FiveM Legacy, GTA, CPU, GPU, memória, armazenamento, cache e
  processos relevantes.
- Modos de otimização Leve, Médio e Agressivo, escolhidos apenas pelo modo (o
  usuário nunca marca tweaks individuais); a `MainViewModel` deriva as opções
  técnicas do perfil selecionado e do diagnóstico.
- Ações reversíveis e restritas para configurações gráficas Legacy, Game Mode,
  preferências de GPU, energia de sessão, captura em segundo plano, efeitos
  visuais e limpezas condicionadas.
- Quatro ações de diagnóstico somente leitura, sempre presentes em todos os
  modos (`ActionOptionGate.Always`, nunca escrevem no sistema, nunca críticas):
  diagnóstico de gargalo provável (memória/CPU/disco), detecção de overlays e
  captura em segundo plano de terceiros conhecidos (NVIDIA, RTSS, Discord,
  Xbox Game Bar — apenas detecta, nunca fecha), leitura do log mais recente do
  FiveM Legacy (contagem aproximada de possíveis erros) e orientação para
  medir desempenho pelos comandos oficiais do próprio FiveM (`cl_drawfps`,
  `cl_drawperf`, `netgraph`, `resmon` — orientação estendida na SEGUNDA FASE
  para citar `resmon` e o painel de streaming) em vez de um benchmark
  embutido — medir FPS exigiria overlay/hook, o que violaria o modelo de
  segurança do produto.
- Cinco ações de diagnóstico somente leitura adicionadas na SEGUNDA FASE
  (23/07/2026), mesmo padrão `ActionOptionGate.Always`: saúde de rede local
  (contadores de pacotes descartados/com erro da placa ativa, sem pingar
  nenhum servidor), temperatura/throttling (leitura best-effort via WMI
  `MSAcpi_ThermalZoneTemperature` com verificação de plausibilidade; informa
  honestamente "não disponível" na maioria dos sistemas sem software do
  fabricante), pagefile/commit (extensão de `SystemResourceSnapshot` com
  `TotalPageFileBytes`/`AvailablePageFileBytes`, nunca redimensiona o
  pagefile), integridade do índice de cache (`content_index.xml` em
  `server-cache`/`server-cache-priv`; só alerta quando o arquivo existe e é
  malformado, recomendando o reparo de cache já existente) e detecção de
  fabricante de GPU (NVIDIA/AMD/Intel a partir do driver já lido no
  diagnóstico de hardware; aponta para o painel oficial do fabricante e nunca
  escreve/sobrescreve perfil de driver). Catálogo de ações agora na
  versão 5 (`ActionCatalog.CurrentVersion`).
- Onze ações de diagnóstico somente leitura adicionadas na TERCEIRA FASE
  (23/07/2026), mesmo padrão `ActionOptionGate.Always`, sem driver de
  kernel/terceiros (decisão explícita do usuário: nada de LibreHardwareMonitor
  ou WinRing0): núcleos/threads/clock de CPU, VRAM e tipo integrada/dedicada
  de GPU (heurística por nome do driver), frequência/canais/XMP-EXPO de RAM
  (heurísticas honestas via WMI, nunca afirmadas como certeza), tipo e saúde
  de unidades físicas (`MSFT_PhysicalDisk`, cobre SATA S.M.A.R.T. e NVMe
  Health sem driver adicional), build do Windows e versões de driver de
  vídeo/rede/áudio/chipset, resolução/taxa de atualização vs. máxima do
  monitor e HAGS (G-SYNC/FreeSync/VRR explicitamente não detectáveis sem
  ferramenta do fabricante — informado, não adivinhado), estado de Modo de
  Jogo/otimizações de tela cheia/plano de energia ativo, um diagnóstico
  **composto** de throttling (combina queda de frequência sob carga, eventos
  WHEA recentes e temperatura ACPI disponível em um único sinal "possível
  throttling", nunca confirmado por sensor direto por núcleo, exatamente
  como o usuário pediu), uso instantâneo de CPU/disco/GPU/rede
  (`PerformanceCounter` + categoria nativa `GPU Engine`, sem driver), link
  PCIe da GPU (best-effort via `CM_Get_DevNode_Property`; falha segura vira
  "não disponível", nunca dado errado) e idade da BIOS + eventos WHEA
  recentes (Resizable BAR/Above 4G/Smart Access Memory explicitamente não
  detectáveis sem ferramenta do fabricante). Catálogo de ações agora na
  versão 6 (`ActionCatalog.CurrentVersion`).
- Sistema de benchmark e comparação antes/depois (23/07/2026), com escopo
  deliberadamente restrito à parte segura do pedido (ver decisão registrada
  abaixo em "Avaliação do sistema de benchmark"): (1) classificador de
  gargalo expandido nas 9 categorias pedidas (GPU/CPU/VRAM/RAM/disco/
  servidor/rede/térmico/processo em segundo plano),
  `BottleneckClassificationAction`, combinando os inspectors já existentes
  mais um novo `IBackgroundProcessInspector`
  (`Win32_PerfFormattedData_PerfProc_Process`, sem driver); "servidor
  limitado" é sempre uma conclusão por eliminação, nunca uma medição direta
  do servidor. (2) Comparação antes/depois: `AppOptimizationService` captura
  um snapshot leve de recursos (CPU/GPU/disco%, memória disponível, sinal
  térmico, problemas de rede) antes de iniciar a otimização e novamente
  depois (com 1s de espera para a atividade da própria otimização assentar),
  e sinaliza regressão apenas para os dois sinais atribuíveis com confiança
  razoável: novo sinal térmico elevado que não existia antes, ou memória
  disponível caindo à menos da metade. Quando sinalizado, a UI mostra
  "Reverter esta otimização", que reaproveita o rollback por transação já
  existente — nunca reverte sozinho. (3) Perfil de hardware:
  `HardwareProfileSignature` gera uma chave SHA-256 estável e local
  (CPU+GPUs+RAM arredondada) para agrupar comparações por máquina; nunca
  identifica servidor do FiveM, pois não há API segura para isso. (4)
  Benchmark oficial do GTA V: `WindowsGtaVBenchmarkRunner` lança o
  `GTA5.exe` standalone com as flags oficiais e documentadas da Rockstar
  (`-benchmark -benchmarkFrameTimes`), nunca dentro de uma sessão do FiveM,
  exige o GTA V fechado antes, roda 3 rodadas e usa a mediana por FPS médio
  (evitando misturar métricas de rodadas diferentes). Como o formato exato
  do arquivo de resultado não é documentado de forma estável entre versões
  do jogo, a busca é defensiva (procura por arquivos `.csv`/`.txt` criados
  após o lançamento contendo "bench"/"frametime" no nome, tenta interpretar
  colunas de frametime ou FPS) e falha honestamente
  ("`benchmark-output-file-not-found`"/"`-not-recognized`") em vez de
  inventar um resultado. Botão dedicado e opt-in em Configurações; nunca faz
  parte do fluxo automático dos perfis Leve/Médio/Agressivo. Catálogo de
  ações avança da versão 6 para a 7 (nova ação de classificação de
  gargalo).
- Relatório técnico agora também pode ser salvo em arquivo
  (`MainViewModel.SaveTechnicalReport`, `SaveFileDialog` nativo no
  code-behind), além de copiado para a área de transferência; usa o mesmo
  `TechnicalReportBuilder`/`ReportSanitizer` sanitizado, e a localização do
  arquivo é sempre escolhida explicitamente pelo usuário.
- Avaliação da "SEGUNDA FASE" pedida pelo usuário em 23/07/2026: rede/jitter,
  temperatura/throttling, pagefile/commit, reparo automatizado (via detecção
  + recomendação) e diagnóstico resmon/netgraph/streaming foram adicionados
  (acima). Relatórios compartilháveis ganharam a opção de salvar em arquivo.
  Dois itens ficaram deliberadamente de fora com a concordância do usuário:
  escrever perfis NVIDIA/AMD/Intel (proibido por `docs/safety.md`; apenas
  detecção/orientação foi implementada) e benchmark por servidor (mesma
  limitação do benchmark da Primeira Fase — exigiria overlay/hook).
- Avaliação da "TERCEIRA FASE" pedida pelo usuário em 23/07/2026 (lista de
  hardware/sistema com legenda ✅/👁, sem nenhum item 🟡/🧪/🔧): antes de
  implementar, o usuário foi consultado especificamente sobre o pedido de
  usar LibreHardwareMonitor "ou biblioteca semelhante" para temperatura, pois
  essas bibliotecas exigem driver de kernel (WinRing0), o que
  `docs/safety.md` proíbe explicitamente. O usuário confirmou: sem driver,
  sem acesso a kernel, coleta best-effort por APIs nativas do Windows,
  informando com honestidade quando um dado não está disponível, e o
  diagnóstico de throttling deve combinar sinais indiretos (queda de clock
  sob carga, uso, WHEA, temperatura disponível) em vez de depender de um
  sensor direto. Todos os 27 itens da lista foram avaliados; os itens de
  detecção pura (nenhum aplica mudança) foram implementados como as onze
  ações descritas acima em "Funcionalidades presentes no código atual".
  Espaço livre em disco e versão/arquitetura do Windows já existiam no
  diagnóstico anterior e não foram duplicados.
- Durante a implementação da TERCEIRA FASE foi corrigido um bug real de
  marshaling em `WindowsDisplayConfigurationInspector`: o P/Invoke de
  `EnumDisplaySettings` usava `CharSet.Auto`/`Ansi` no struct `DEVMODE`, mas
  o Windows moderno resolve `CharSet.Auto` para a variante Unicode
  (`EnumDisplaySettingsW`); a incompatibilidade desalinhava todos os campos
  após `dmDeviceName` e produzia uma chamada "bem-sucedida" com resolução e
  taxa de atualização zeradas em vez de uma falha limpa. Corrigido fixando
  `CharSet.Unicode` de ponta a ponta (struct + `EntryPoint =
  "EnumDisplaySettingsW"`); confirmado com dados reais da máquina de
  desenvolvimento antes do commit.
- Avaliação do sistema de "Benchmark e comparação antes/depois" pedido pelo
  usuário em 23/07/2026: antes de implementar, o usuário foi consultado
  sobre três pontos que tensionavam decisões já documentadas. (1) Captura de
  FPS/frametime/1%-low **ao vivo dentro de uma sessão real do FiveM**
  (diferenciando parado/dirigindo/urbano/servidor cheio) exigiria ETW
  (reconstruir algo como o PresentMon da Microsoft) ou hook/injeção
  (proibido); o usuário confirmou **não implementar por enquanto**, mantendo
  a decisão já registrada nas Fases 1 e 2 — nenhum código de ETW/DXGI foi
  escrito. (2) O parâmetro oficial `-benchmark` do GTA V (fora do FiveM,
  sequência fixa, lê arquivo de log) foi confirmado como seguro e útil —
  implementado como `WindowsGtaVBenchmarkRunner`. (3) "Reverter
  automaticamente ajustes que piorarem o resultado" tensionava com o
  princípio de "prévia completa"; o usuário confirmou **sinalizar e pedir
  confirmação** em vez de reverter sozinho — implementado como
  `MainViewModel.CanRevertLastOptimization`/`RevertLastOptimizationAsync`,
  que reaproveita o rollback por transação já existente. Como consequência
  dessas decisões, "diferenciar parado/dirigindo/urbano/servidor cheio" e
  "salvar histórico por servidor" não foram implementados (exigiriam a
  captura de FPS ao vivo ou uma forma segura de identificar o servidor
  conectado, que não existe sem ler estado do FiveM); "salvar perfil por
  hardware" foi implementado sem a dimensão "por servidor".
- Motor de execução **isolada por ação** (`WindowsTransactionOptions.
  IsolateFailures`, usado pelo fluxo padrão do app): cada ação verifica,
  aplica, valida e registra separadamente; uma falha reverte só a própria
  ação; pré-requisito não atendido gera `Skipped`; falha crítica (verificação
  de processo FiveM/GTA V) aborta as ações independentes restantes
  (`NotRun`); a run nunca é reportada como sucesso total se qualquer ação
  falhou (`ActionExecutionOutcome`, `WindowsTransactionState.
  CommittedWithErrors`). O broker elevado continua no modo estrito original
  (poucas ações administrativas, tipicamente uma).
- Progresso estruturado: etapa X de N, outcome por etapa e livro-razão ao vivo
  na interface (`MainViewModel.StepLedger`), além de percentual, tempo
  decorrido e estimativa de tempo restante já existentes.
- Relatório final estruturado (`OptimizationReportDto`/
  `OptimizationReportBuilder`, construído a partir do journal local) com
  contagens de verificado/alterado/ignorado/aviso/falha, necessidade de
  reinício e possibilidade de restauração; botão "Copiar relatório técnico"
  gera texto sanitizado (`TechnicalReportBuilder`/`ReportSanitizer`, sem nomes
  de usuário em caminhos, sem tokens/credenciais) via área de transferência.
- Apresentação estruturada de cada modo (benefícios, nível de impacto, riscos,
  reversibilidade, categorias analisadas e aviso de variação por computador),
  derivada do catálogo por `ProfilePresentationProvider` para nunca divergir
  do plano real.
- Documentação por ação de primeira classe no catálogo (`ActionMetadataDto`):
  pré-requisitos, criticidade, versões do Windows suportadas, detecção,
  confirmação, desfazer e riscos/limitações; usada pelo motor (dependência e
  gating por versão do Windows) e disponível para a revisão de plano.
- Journal transacional, snapshots e rollback por ação.
- Broker elevado de escopo mínimo, atualização opcional por GitHub Releases,
  atalho de desenvolvimento, ícone oficial, bandeja, inicialização opcional,
  suporte de idioma/tema e formulário de bugs.
- Painel local de prontidão para criadores: reconhece OBS, Streamlabs Desktop e
  TikTok LIVE Studio, preserva seus processos e exibe sinais de software,
  recursos e sessão de jogo sem inferir que uma live está ativa nem alterar
  cenas, contas, encoder ou gravações.
- Instalador Inno Setup, pacote portável self-contained, manifestos e checksums
  de release.
- Landing page e documentação de segurança, instalação, pesquisa, bugs e
  streaming.

## Limitações e cuidados conhecidos

- Não há promessa de FPS ou de ausência de falso positivo em todos os
  antivírus. A versão sem assinatura Authenticode pode receber avisos de
  reputação/SmartScreen.
- O produto não desativa Defender, firewall, SmartScreen, UAC, Windows Update
  ou serviços essenciais; não cria exclusões de antivírus, não injeta código,
  não altera memória nem baixa/executa código arbitrário.
- Não há suporte operacional para GTAV Enhanced.
- Testes que alterariam uma instalação real de Windows/FiveM são opt-in; a
  suíte padrão usa doubles e diretórios temporários.
- O broker elevado (ações administrativas) continua no modo estrito
  tudo-ou-nada; a execução isolada por ação vale para o fluxo padrão do app
  (`AppOptimizationService`), que é onde está a maioria das ações do plano.
- Nenhuma otimização nova foi adicionada nesta etapa; o trabalho foi
  inteiramente sobre motor de execução, progresso, relatório e apresentação
  das otimizações já existentes e pesquisadas em `docs/research.md`.
- O instalador público e o sistema de atualização automática estão implementados
  e documentados na seção de distribuição; a publicação é deliberadamente manual
  por workflow_dispatch para evitar releases acidentais.
- Build, lint, testes renderizados e `npx tsc --noEmit` do site passam. A
  landing estática do GitHub Pages não requer runtime Cloudflare no navegador.
- O conteúdo de `website/` faz parte do repositório principal. Seus artefatos
  gerados e credenciais locais são ignorados tanto pela raiz quanto pelo
  `.gitignore` específico do site.

## Comandos de desenvolvimento e validação

## Validação e handoff atual

- Esta etapa (motor de otimização resiliente, progresso estruturado,
  relatório e apresentação de modos) está pronta e integrada em `main`.
  Commits locais desta tarefa, do mais antigo ao mais recente:
  - `f530afb` docs: especificação do motor de otimização resiliente
  - `f782e59` feat(core,engine): execução isolada por ação, outcomes e relatório
  - `991856b` feat(app): progresso estruturado, relatório final e apresentação de modos
  - este commit (docs: atualiza safety/architecture/PROJECT_STATE) fecha a
    etapa; use `git log --oneline -6` para conferir o hash exato.
- O checkout canônico está em `C:\Projetos\FiveMCleaner`, no branch `main`.
- Especificação completa da tarefa em
  `docs/superpowers/specs/2026-07-22-motor-otimizacao-resiliente-design.md`.
- Última validação do app: `dotnet build` Release sem avisos/erros, **238
  testes .NET aprovados** cobrindo isolamento,
  dependência, aborto crítico, outcome, relatório, sanitização e apresentação
  de modos) e `scripts\Verify-Safety.ps1` aprovado. O executável real abriu e
  permaneceu estável por smoke test manual (`Start-Process` + `--demo-synthetic`,
  5s, sem novas entradas em `crash.log`) exercitando as novas telas de
  progresso/relatório/apresentação de modo.
- Área ainda não coberta por teste automatizado nesta etapa: a integração fim
  a fim `AppOptimizationService → runtime real do Windows` (só é exercitada
  por doubles no motor; o serviço de app em si depende de Windows real). É um
  bom próximo passo para o agente seguinte, se quiser reforçar cobertura.
- Última validação do site: lint, typecheck, build e testes renderizados
  aprovados. A landing estática também é verificada quanto à presença do
  instalador direto, do ícone oficial e da ausência do endereço anterior.
- O push desta tarefa para `origin/main` foi autorizado explicitamente pelo
  usuário e realizado ao final desta etapa, sem PR — confira `git log
  origin/main` para confirmar que o HEAD local e o remoto coincidem antes de
  iniciar trabalho novo. Não há alterações pendentes nem artefatos locais
  versionáveis esperados após o push.

Na raiz do repositório:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
.\scripts\Verify-Safety.ps1
dotnet run --project src\FiveMCleaner.App\FiveMCleaner.App.csproj
.\scripts\Start-DevelopmentApp.ps1
.\scripts\Install-DevelopmentShortcut.ps1
.\scripts\Build-Portable.ps1
.\scripts\Build-Installer.ps1 -Version <versão>
```

Para o site:

```powershell
Set-Location website
npm ci
npm run build
npm run lint
npm test
npx tsc --noEmit
```

O `npm test` do site já executa o build antes dos testes de HTML renderizado.
Use `docs/installer.md`, `docs/safety.md` e `docs/architecture.md` como contexto
complementar, mas confirme sempre o comportamento no código e nos testes.

## Distribuição e validação (atualização de 23/07/2026)

- A distribuição pública atual é a versão `v1.0.2` e usa Inno Setup 6.7.3, com aplicativo e broker
  `win-x64` self-contained: não requer .NET, Node.js, SDK, Visual Studio ou
  outra ferramenta de desenvolvimento na máquina da pessoa.
- O instalador é por usuário, detecta pt-BR/inglês pela linguagem de interface
  atual do Windows (inglês como fallback) e tema do Windows, oferece
  atalho de área de trabalho e inicialização com Windows, atualiza por cima da
  instalação anterior e usa Restart Manager sem encerramento forçado. Na
  desinstalação interativa, a pessoa escolhe preservar ou remover
  `%LOCALAPPDATA%\FiveMCleaner`; em modo silencioso a preservação é o padrão.
- O atualizador consulta exclusivamente `/releases/latest` do repositório
  oficial, aceita somente SemVer estável e instalador allowlisted, exige HTTPS,
  tamanho e digest SHA-256 do GitHub, grava o download de forma atômica e pede
  confirmação antes de abrir o setup. A interface só abre notas da release na
  página oficial da tag. Falha de download não altera a versão instalada.
- `CHANGELOG.md` é a fonte das alterações públicas. O workflow
  `.github/workflows/release.yml` só publica por `workflow_dispatch`, após
  build, testes, smoke de instalação/upgrade/desinstalação, checksums, manifesto
  e atestação de proveniência. Consulte `docs/installer.md` para versionar,
  etiquetar, disparar e verificar uma release.
- Validação executada nesta etapa: build .NET Release sem avisos/erros, 238
  testes xUnit aprovados, `Verify-Safety.ps1` e contrato do instalador aprovados;
  lint, typecheck, build e testes renderizados do site aprovados.
- `npm audit --omit=dev --audit-level=high` ainda indica dois alertas altos
  transitivos em `sharp`/`next`. O registro não oferece correção não disruptiva
  para a versão disponível; não usar `npm audit fix --force` sem revisar a
  compatibilidade Vinext/Next. Esta limitação não deve ser ocultada.
- A página pública de download é publicada gratuitamente no GitHub Pages em
  `https://marquezinii.github.io/FiveMCleaner/`. O workflow
  `.github/workflows/pages.yml` publica somente o conteúdo estático de
  `website/public-site/` depois de mudanças em `main`. Ela é vinculada pelo link
  sublinhado **DOWNLOAD** no topo do `README.md` exibido no GitHub.
- O identificador da hospedagem anterior foi removido do checkout e o site
  correspondente foi restringido à conta do proprietário. O arquivo
  `website/.openai/hosting.json` permanece apenas com bindings locais vazios,
  necessários ao build Vinext; ele não contém URL ou credencial. A única
  página pública promovida pelo projeto é a do GitHub Pages.
- Os botões da landing page iniciam o download direto do alias estável
  `FiveMCleaner-Setup-latest-win-x64.exe`, hospedado no GitHub Releases. Em toda
  release estável, `release.yml` publica esse alias além do instalador
  versionado. O atualizador do aplicativo **não** usa o alias: continua exigindo
  o arquivo versionado, HTTPS, tamanho e SHA-256 publicados pela API do GitHub.
- A numeração pública segue a sequência exigida pelo produto: começa em
  `1.0.0`, incrementa patch até `X.Y.99` e então avança para `X.(Y+1).0`.
  `scripts/Test-PublicVersionProgression.ps1` aplica essa regra em releases
  estáveis, com uma exceção única documentada para a transição histórica de
  `v0.2.0` para `v1.0.0`.
- A arte lateral do instalador é gerada localmente por
  `scripts/New-InstallerArtwork.ps1` a partir do ícone oficial. Ela usa a
  proporção 164:314 exigida pelo Inno Setup, preservando o ícone quadrado sem
  distorção. O setup não reaproveita mais o idioma de uma instalação anterior:
  sempre reavalia o idioma atual do Windows ao iniciar.
- A fase administrativa possui watchdog duplo: o broker encerra uma etapa
  elevada que exceder 90 segundos com resultado de falha seguro e o aplicativo
  deixa de aguardar uma resposta do broker após dois minutos. Não há sucesso
  implícito nem nova tentativa automática; o journal e o relatório preservam o
  estado para diagnóstico e rollback quando aplicável.

## Revisão visual (atualização de 22/07/2026)

- A interface prioriza tipografia Segoe UI Variable: `Display` para títulos de
  seção e `Text` para conteúdo e metadados. Chips usam altura mínima e
  alinhamento vertical explícito para preservar o enquadramento em DPI maior.
- O status detectado do FiveM usa sinal verde com check; os cards de modo são
  mais minimalistas e exibem selo somente no perfil Médio, recomendado.
- O diagnóstico de hardware exibe armazenamento arredondado sem a indicação de
  espaço livre e, quando o WMI disponibiliza os módulos, informa a composição
  física da RAM (por exemplo, `32 GB · 2×16 GB`). Idioma e aparência usam
  seletores compactos; minimizar para a bandeja é um toggle único.
- A visão geral mostra FiveM Legacy e GTA V Legacy com um estado binário e
  coerente: check verde para detectado e X vermelho para não detectado. A
  identificação do Windows considera o build 22000 ou superior como Windows
  11, porque o sistema conserva internamente a versão `10.0` por
  compatibilidade. Os modos usam velocímetros neutros com ponteiro verde,
  amarelo ou vermelho para comunicar a intensidade; os avisos redundantes de
  recomendação e os cards de streaming não aparecem na interface principal.
- A landing page em `website/` permanece a experiência React usada em ambientes
  de desenvolvimento. A versão pública equivalente fica em
  `website/public-site/`, é estática, responsiva e publicada por GitHub Pages
  com identidade visual escura/laranja. O GitHub Releases continua sendo a
  origem única e verificável do instalador, enquanto a página serve apenas como
  central de apresentação e inicia o download direto do arquivo oficial.
- O rodapé de suporte agora é global e fixado abaixo do conteúdo principal: o
  atalho **Relatar um bug** e o copyright continuam acessíveis também com a
  janela maximizada. Os seletores de idioma e aparência usam templates WPF
  próprios (campo, popup e itens), todos vinculados à paleta do aplicativo,
  para não voltar ao fundo branco do controle padrão do Windows no tema escuro.
- A janela principal trata `WM_GETMINMAXINFO` para maximizar na área útil do
  monitor atual, em pixels nativos. Isso evita tanto o rodapé sob a barra de
  tarefas quanto faixas vazias em múltiplos monitores ou escalas de DPI altas
  com `WindowChrome` personalizado. O link de relato preserva apenas cursor e
  sublinhado: não usa hover ou tooltip visual.
- O card **Proteção ativa** mostra apenas o estado compacto; a explicação de
  snapshot e rollback fica no tooltip. Os seletores de configurações usam
  recuo interno maior, o subtítulo redundante da página foi removido, o selo do
  perfil selecionado é verticalmente centralizado e apenas a moldura do botão
  de fechar fica vermelha ao passar o mouse.
- O status de proteção é alinhado verticalmente ao escudo e exibe a versão
  instalada logo abaixo. O `Padding` do seletor é repassado ao botão interno do
  template WPF, garantindo o recuo visual do valor selecionado em qualquer DPI.
- A versão exibida no painel lateral é uma leitura unidirecional da montagem.
  Isso evita que o binding de um `Run` tente escrever em `AppVersion`, que é
  uma propriedade calculada e somente leitura.
- A versão lateral usa um selo compacto, localizado e de leitura unidirecional,
  em vez de texto solto. A página pública de download possui a seção
  **Última versão pública**, alimentada apenas pelo conteúdo factual do
  `CHANGELOG.md`; ela deve ser atualizada junto da próxima release autorizada.
- O número exibido nesse selo usa `TextBrush`, um recurso presente em todos os
  temas. Não usar `TextPrimaryBrush`: ele não existe na paleta e faz o WPF cair
  na cor padrão preta em vez de preservar o contraste do tema escuro.
- Durante uma otimização, o botão de cancelar e qualquer tentativa de fechar o
  app passam pelo modal temático `OptimizationConfirmationWindow`. A recusa
  preserva a execução; a confirmação solicita cancelamento seguro somente após
  a etapa atual. No caso de fechamento, a janela permanece aberta até a rotina
  encerrar e só então fecha. Logoff/desligamento do Windows continua sem modal
  para não bloquear o sistema.
- A lista do plano atual foi simplificada: mantém nome e descrição de cada ação,
  mas não expõe chips internos de risco, reversibilidade, privilégio ou versão
  do catálogo. O espaçamento do título de Configurações segue agora o padrão
  das demais páginas.

## Publicação v1.0.2 e handoff (23/07/2026)

- A tag `v1.0.2` aponta para `74f23ebab836902fe19d9dea7f4ae9c4fd17e31a` e a
  release pública está disponível em
  `https://github.com/marquezinii/FiveMCleaner/releases/tag/v1.0.2`.
- O workflow de release `30034666597` passou integralmente: validação de versão,
  segurança e testes, pacote autocontido, instalador, smoke test de instalação/
  atualização/desinstalação, hashes, manifesto e atestação de procedência.
- O GitHub Pages para o mesmo commit também passou (`30034659354`). A página
  pública e o download direto do alias estável responderam HTTP 200 após a
  publicação; a página exibe a seção **Última versão pública** para `1.0.2`.
- O commit posterior de documentação de handoff não altera arquivos do app,
  instalador, site público ou versão. Ele é permitido pela exceção de governança
  registrada em `AI_RULES.md` e deve permanecer separado de futuras releases.

## Diagnósticos somente leitura da PRIMEIRA FASE (23/07/2026)

- Trabalho local, **não publicado** (sem push/tag/release desta etapa, por
  instrução explícita do usuário). Não altera versão pública, instalador nem
  site.
- Avaliação completa dos 10 itens pedidos pelo usuário e decisões registradas
  acima em "Decisões técnicas e padrões". Resumo: 5 já existiam
  (backup/restauração transacional, presets do `settings.xml`, cache seguro,
  plano de energia temporário, GPU de alto desempenho); 3 foram adicionados
  como diagnósticos somente leitura sempre ativos (gargalo, overlays/captura,
  leitura de log do FiveM), mais a orientação de comandos oficiais de FPS/rede
  no lugar de um benchmark embutido; 1 item (modo sessão que fecha/reabre
  apps) ficou fora por risco real de perda de dados, com a concordância do
  usuário.
- Novo código: `src/FiveMCleaner.Windows/Actions/DiagnosticActions.cs` (4
  ações), `src/FiveMCleaner.Windows/Infrastructure/SystemResourceInspector.cs`
  e `OverlaySoftwareInspector.cs` (leituras read-only atrás de interface,
  testáveis com doubles). Catálogo de ações avançou da versão 3 para a 4.
- Validação: `dotnet build` Release sem avisos/erros; **258 testes .NET
  aprovados** (244 anteriores + 14 novos cobrindo classificação de gargalo,
  degradação segura quando a leitura de hardware falha, detecção de overlays,
  leitura/priorização do log mais recente, mensagem de orientação de FPS, e
  as duas novas infraestruturas reais); `scripts\Verify-Safety.ps1` aprovado;
  smoke test manual do executável (`Start-Process --demo-synthetic`, 5s, sem
  novas entradas em `crash.log`).
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Diagnósticos somente leitura da SEGUNDA FASE (23/07/2026)

- Trabalho local, **não publicado** (sem push/tag/release desta etapa, por
  instrução explícita do usuário). Não altera versão pública, instalador nem
  site.
- Avaliação completa dos 8 itens pedidos pelo usuário e decisões registradas
  acima em "Decisões técnicas e padrões". Resumo: rede/jitter,
  temperatura/throttling, pagefile/commit, integridade do índice de cache
  (reparo automatizado) e a extensão de resmon/streaming à orientação de
  performance foram adicionados como diagnósticos somente leitura; o item de
  relatórios compartilháveis ganhou "Salvar em arquivo" além de "Copiar".
  Dois itens ficaram deliberadamente restritos a detecção/orientação, com a
  concordância do usuário: perfis NVIDIA/AMD/Intel (só detecta fabricante e
  aponta para o painel oficial; nunca escreve perfil, por proibição explícita
  de `docs/safety.md`) e benchmark por servidor (não implementado; mesma
  limitação do benchmark da Primeira Fase).
- Novo código: 5 ações em
  `src/FiveMCleaner.Windows/Actions/DiagnosticActions.cs` (rede, térmica,
  pagefile, integridade de cache, fabricante de GPU) e 3 novos inspectors em
  `src/FiveMCleaner.Windows/Infrastructure/` (`NetworkHealthInspector.cs`,
  `ThermalInspector.cs`, `GpuVendorInspector.cs`), todos read-only atrás de
  interface e testáveis com doubles; `SystemResourceSnapshot` ganhou campos
  de pagefile. Referência a `System.Management` (WMI) adicionada ao projeto
  `FiveMCleaner.Windows`, seguindo o mesmo padrão try/fallback-honesto já
  usado em `AppOptimizationService` para composição de módulos de RAM.
  Catálogo de ações avançou da versão 4 para a 5.
- App: `MainViewModel.SaveTechnicalReport` + `SaveFileDialog` nativo no
  code-behind para exportar o relatório técnico sanitizado como arquivo,
  complementando a cópia para a área de transferência já existente.
- Validação: `dotnet build` Release sem avisos/erros; **277 testes .NET
  aprovados** (258 anteriores + 19 novos cobrindo classificação de rede,
  temperatura, pagefile, integridade de cache, detecção de fabricante de GPU
  e as três novas infraestruturas reais); `scripts\Verify-Safety.ps1`
  aprovado; smoke test manual do executável (`Start-Process
  --demo-synthetic`, 5s, sem novas entradas em `crash.log`).
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Diagnósticos somente leitura da TERCEIRA FASE (23/07/2026)

- Trabalho local, **não publicado** (sem push/tag/release desta etapa, por
  instrução explícita do usuário). Não altera versão pública, instalador nem
  site.
- Antes de implementar, o usuário foi consultado sobre o pedido explícito de
  LibreHardwareMonitor/biblioteca semelhante para temperatura (exige driver
  de kernel, proibido por `docs/safety.md`). Decisão registrada acima em
  "Avaliação da TERCEIRA FASE": sem driver, coleta best-effort por API
  nativa do Windows, honestidade quando um dado não está disponível, e
  throttling diagnosticado por combinação de sinais indiretos, não por
  sensor direto.
- Novo código: 11 ações em
  `src/FiveMCleaner.Windows/Actions/HardwareDiagnosticActions.cs` (CPU, GPU,
  RAM, armazenamento, drivers, monitor, sessão/energia, throttling
  composto, uso de recursos, link PCIe, estabilidade de hardware) e 9 novos
  inspectors em `src/FiveMCleaner.Windows/Infrastructure/`
  (`CpuInspector.cs`, `GpuDetailsInspector.cs`, `RamDetailsInspector.cs`,
  `StorageHealthInspector.cs`, `DriverVersionInspector.cs`,
  `DisplayConfigurationInspector.cs`, `ResourceUsageInspector.cs`,
  `PciLinkInspector.cs`, `HardwareStabilityInspector.cs`), todos read-only
  atrás de interface e testáveis com doubles. O diagnóstico de GPU vendor da
  Segunda Fase foi preservado sem alteração; VRAM/tipo integrada-dedicada
  entraram por um inspector novo e separado para não arriscar o código já
  testado. Pacotes adicionados ao projeto `FiveMCleaner.Windows`:
  `System.Diagnostics.PerformanceCounter` (uso de CPU/disco/GPU). Catálogo
  de ações avançou da versão 5 para a 6.
- Bug real corrigido durante a implementação (não é apenas um ajuste de
  teste): marshaling incorreto de `EnumDisplaySettings` em
  `WindowsDisplayConfigurationInspector` retornava sucesso com todos os
  campos zerados em vez de dados reais ou uma falha limpa. Ver detalhes
  acima em "Decisões técnicas e padrões". Sem esse conflito de `CharSet`, a
  leitura de resolução/taxa de atualização/HAGS estaria silenciosamente
  quebrada em produção.
- Limitações conhecidas e assumidas conscientemente, documentadas nas
  mensagens do próprio app em vez de escondidas: G-SYNC/FreeSync/VRR não têm
  API pública sem driver do fabricante; Resizable BAR/Above 4G/Smart Access
  Memory não são detectáveis com confiança sem ferramenta do fabricante; o
  link PCIe só é lido quando o driver/placa-mãe expõe as DEVPKEYs padrão do
  Windows (não em toda combinação de hardware); a classificação de fabricante
  de RAM single-channel/XMP é heurística, não uma leitura de BIOS garantida;
  temperatura só é reportada quando a zona térmica ACPI do WMI retorna um
  valor plausível, o que não acontece na maioria dos PCs sem software do
  fabricante — em todos esses casos o app diz "não disponível" em vez de
  inventar um número.
- Validação: `dotnet build` Release sem avisos/erros; **317 testes .NET
  aprovados** (277 anteriores + 40 novos cobrindo classificação de CPU/GPU/
  RAM/armazenamento/drivers/monitor/sessão/throttling composto/uso de
  recursos/link PCIe/estabilidade de hardware, mais smoke tests reais de
  cada um dos 9 novos inspectors); `scripts\Verify-Safety.ps1` aprovado;
  smoke test manual do executável (`Start-Process --demo-synthetic`, 6s, sem
  novas entradas em `crash.log`).
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Benchmark e comparação antes/depois (23/07/2026)

- Trabalho local, **não publicado** (sem push/tag/release desta etapa, por
  instrução explícita do usuário). Não altera versão pública, instalador nem
  site.
- Antes de implementar, o usuário foi consultado sobre três pontos que
  tensionavam decisões já registradas (captura de FPS ao vivo, benchmark
  oficial do GTA V, reversão automática). Decisões e justificativas completas
  registradas acima em "Avaliação do sistema de benchmark".
- Novo código:
  - `src/FiveMCleaner.Windows/Infrastructure/BackgroundProcessInspector.cs` —
    processo com maior uso de CPU, excluindo FiveM/GTA/o próprio app
    (`Win32_PerfFormattedData_PerfProc_Process`, sem driver);
  - `BottleneckClassificationAction` (em `HardwareDiagnosticActions.cs`) —
    9ª ação de diagnóstico sempre presente, combina os inspectors já
    existentes para classificar GPU/CPU/VRAM/RAM/disco/servidor/rede/
    térmico/processo-de-fundo limitado, em ordem de prioridade;
  - `src/FiveMCleaner.App/Services/HardwareProfileSignature.cs` — assinatura
    SHA-256 estável de CPU+GPUs+RAM, local, nunca identifica servidor;
  - `AppOptimizationService.TryCaptureResourceComparisonSnapshot`/
    `BuildComparison`/`ComputeRegressionReasonKeys` — captura antes/depois e
    regra de regressão conservadora (só sinaliza novo problema térmico ou
    memória disponível caindo à menos da metade), nunca deriva de FPS;
  - `MainViewModel.RevertLastOptimizationAsync`/`CanRevertLastOptimization` —
    reaproveita o rollback por transação já existente, nunca reverte sem
    clique do usuário;
  - `src/FiveMCleaner.Windows/Infrastructure/GtaVBenchmarkRunner.cs` —
    lança `GTA5.exe -benchmark -benchmarkFrameTimes` standalone (nunca
    dentro do FiveM), exige GTA V fechado, roda N rodadas, busca o arquivo
    de resultado de forma defensiva (nome/local do arquivo não é
    documentado de forma estável entre versões do jogo) e falha
    honestamente quando não consegue localizar/interpretar o resultado;
    calcula FPS médio/mínimo, 1%/0,1% low e frametime médio/pico por
    rodada, com mediana por FPS médio entre rodadas (nunca mistura métricas
    de rodadas diferentes); exposto via `IAppOptimizationService.
    RunGtaVBenchmarkAsync` e um botão dedicado e opt-in em Configurações —
    nunca faz parte do fluxo automático dos perfis.
- Catálogo de ações avançou da versão 6 para a 7 (nova ação de classificação
  de gargalo). O benchmark oficial do GTA V e a comparação antes/depois não
  são ações de catálogo: o primeiro lança um processo externo de vida longa
  (não cabe no modelo de ação curta e transacional), e a segunda é um
  extra informativo em volta de uma otimização já existente, não uma
  otimização em si.
- Limitação conhecida e assumida conscientemente: o formato exato do
  arquivo de resultado do benchmark oficial do GTA V não pôde ser verificado
  com certeza neste ambiente (sem acesso à internet durante o
  desenvolvimento); o parser busca arquivos plausíveis e tenta interpretar
  colunas de frametime/FPS de forma genérica, reportando falha honesta
  (`benchmark-output-file-not-found`/`-not-recognized`) em vez de um
  resultado inventado quando o formato real não corresponder. Recomenda-se
  validar em uma máquina com GTA V instalado antes de divulgar este recurso
  amplamente.
- Validação: `dotnet build` Release sem avisos/erros; **353 testes .NET
  aprovados** (317 anteriores + 36 novos cobrindo as 9 categorias do
  classificador de gargalo, a assinatura de hardware, a regra de detecção
  de regressão, o parser do benchmark oficial do GTA V — incluindo formatos
  válidos, delimitador `;`, arquivo não reconhecido, poucas amostras,
  arquivo ausente — a mediana entre rodadas e a validação/modo demo do
  serviço de benchmark); `scripts\Verify-Safety.ps1` aprovado; smoke test
  manual do executável (`Start-Process --demo-synthetic`, 6s, sem novas
  entradas em `crash.log`) exercitando as novas telas.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.
