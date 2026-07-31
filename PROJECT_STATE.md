# Estado do Projeto

## Refactoring pass do instalador — 31/07/2026

- `Assert-UnderArtifacts` estava duplicada, byte a byte, em
  `scripts/Build-Installer.ps1` e `scripts/Test-Installer.ps1`. Extraída para
  `scripts/Installer.Common.ps1` (nova função `Assert-PathUnderRoot`,
  parametrizada por `-Root` em vez de depender de uma variável de escopo),
  dot-sourced pelos dois scripts. Cada script mantém um wrapper local
  `Assert-UnderArtifacts` de uma linha para não precisar tocar nas chamadas
  existentes.
- Em `Test-Installer.ps1`, as três listas de argumentos do Inno Setup
  (instalação, upgrade, desinstalação) repetiam
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`; agora vêm de
  `$commonSilentArguments`, reaproveitado também pela desinstalação de
  limpeza no bloco `finally`.
- `scripts/Build-Portable.ps1` tem a mesma função duplicada, mas foi
  deixado de fora por não ser exclusivo do instalador (também serve o
  pacote portátil standalone) — fora do escopo pedido nesta rodada.
- Nenhuma mudança de comportamento: mesmos parâmetros, mesmas mensagens de
  erro, mesmos argumentos passados ao Inno Setup. Validação: build Release
  sem avisos, 597 testes .NET, `Verify-Safety.ps1`,
  `Verify-Installer.ps1 -ScriptOnly`, `Build-Installer.ps1` completo (gerou
  o instalador `1.1.3` de novo) e `Test-Installer.ps1` real (instalação,
  upgrade in-place e desinstalação silenciosas) todos aprovados.

## Rodada de audit and remediation — 31/07/2026

- Auditoria manual (sem ferramenta automatizada) cobrindo instalador/updater
  .NET, broker elevado, e o Worker Cloudflare (auth, CORS, queries D1,
  dashboard). A maior parte do código já é defensiva (allowlist de paths,
  SQL sempre parametrizado, cookies HttpOnly/Secure, PBKDF2 com comparação
  em tempo constante); nenhum problema crítico novo encontrado nessas
  camadas.
- Bug real corrigido: `UpdateHealthReceiptStore.Confirms`,
  `UpdateRecoveryJournal.TryRead` e `RecoveryCoordinator.Reconcile` liam
  `health.json`/`recovery.json`/`active.json` sem tratar `IOException`/
  `UnauthorizedAccessException` transitórias (por exemplo, um antivírus
  segurando o arquivo por alguns milissegundos durante o `File.Replace`
  concorrente de outro processo). Isso propagava para
  `FiveMCleaner.Launcher`, que exibia um erro ao usuário e recusava abrir o
  app por causa de um lock passageiro, não de um problema real de
  recuperação. As três leituras agora tratam essa falha transitória como
  "ainda não confirmado"/"pendente" em vez de propagar, sem alterar o
  comportamento de corrupção real (JSON malformado continua sendo
  quarentenado como antes).
- Cobertura nova: um teste por store, cada um segurando o arquivo com
  `FileShare.None` para reproduzir o lock de forma determinística e
  confirmar que a leitura seguinte, já sem o lock, volta a funcionar
  normalmente.
- Validação: build Release sem avisos, 597 testes .NET (3 novos) e
  `Verify-Safety.ps1` aprovados. Nenhuma mudança no Worker Cloudflare, no
  broker ou no instalador Inno Setup nesta rodada — a auditoria os revisou,
  mas não encontrou correção necessária ali.

## Hardening agressivo da cadeia do instalador — 31/07/2026

- `SilentUpdateInstaller.CopyUpdaterOutsideInstallDirectory` agora recalcula
  SHA-256 duas vezes: logo após a cópia para o arquivo temporário (antes do
  rename) e de novo no caminho final, imediatamente antes de retornar para o
  `Process.Start`. Isso fecha a janela TOCTOU entre copiar/renomear o
  atualizador independente para `%LOCALAPPDATA%` e executá-lo, contra troca
  do arquivo por outro processo do mesmo usuário nesse intervalo.
- `FiveMCleaner.Updater` (processo que roda o setup silencioso) agora limita
  `WaitForExit` a 10 minutos; se o Inno Setup travar (por exemplo, uma caixa
  de diálogo do Restart Manager que escapou de `/SUPPRESSMSGBOXES`), o
  atualizador mata a árvore de processos e reporta timeout em vez de
  bloquear indefinidamente sem feedback ao usuário.
- `Verify-Installer.ps1` passou a travar também `RestartIfNeededByRun=no` no
  contrato do `.iss`, impedindo que uma edição futura do script reintroduza
  reinício automático de máquina após a instalação silenciosa.
- Escopo desta rodada foi deliberadamente restrito ao instalador e a tudo que
  o cerca (Inno Setup, `FiveMCleaner.Updater`, cópia/handoff do atualizador,
  contrato de verificação); nenhuma mudança em diagnóstico, perfis ou
  telemetria. Validação: build Release sem avisos, 594 testes .NET,
  `Verify-Safety.ps1` e `Verify-Installer.ps1 -ScriptOnly` aprovados.

## SemVer de patch com múltiplas casas — 31/07/2026

- A regra de publicação explicita que `X.Y.Z` continua sendo SemVer: `Z` é
  um inteiro decimal sem largura fixa, portanto `1.1.10`, `1.1.99` e
  `1.1.100` são patches válidos; `X.X.XX` é somente uma forma visual de
  indicar duas ou mais casas no último componente, não uma versão fracionária
  nem um quarto componente.
- O selo da versão na barra lateral ganhou largura mínima para acomodar o
  patch de duas casas. O parser já usava componentes numéricos arbitrários;
  testes de contrato agora cobrem aceitação e ordenação numérica de
  `1.1.9`, `1.1.10` e `1.1.99`.
- O smoke do instalador mantém a proteção padrão contra instalação existente.
  Para este ambiente de desenvolvimento, a chave explícita
  `-AllowExistingInstallation` permite executar a prova isolada quando o
  operador autorizar conscientemente a alteração temporária do registro.
- Validação desta rodada: build Release sem avisos, 594 testes .NET, safety
  check, contrato do instalador e smoke real de instalação silenciosa,
  upgrade in-place e desinstalação aprovados com o payload atual. O smoke usou
  a exceção explicitamente autorizada para a instalação existente e removeu a
  árvore temporária ao final; um Windows limpo continua sendo o gate externo
  de uma futura publicação oficial.

## Arquitetura de próxima geração do updater — 31/07/2026

- O fluxo foi conectado de ponta a ponta no código: o app consulta o manifesto
  estável assinado no Worker, valida ECDSA P-256/SHA-256 com chave pública
  incorporada, TLS/revogação, host, tamanho, hash, SemVer e piso
  anti-downgrade antes de baixar o ZIP de runtime.
- `FiveMCleaner.Launcher.exe` é o único alvo de atalhos e inicialização. O app
  fica em `Runtime/versions/<versão>`; staging valida `SHA256SUMS.txt` fechado
  (arquivos ausentes, extras, duplicados ou alterados são rejeitados) e
  `active.json` é trocado atomicamente.
- A primeira inicialização da candidata usa journal, nonce e health receipt.
  Falha, saída precoce ou timeout de 45 segundos restaura apenas o predecessor
  registrado. A maior versão saudável é persistida com DPAPI e o launcher
  recusa um ponteiro abaixo desse piso.
- O instalador Inno permanece somente como entrada inicial/transição para PCs
  que ainda usam o layout legado; ele instala o launcher e a primeira árvore
  imutável. Atualizações seguintes não executam nem substituem o instalador.
- Reavaliado o requisito de custo zero: MSIX/App Installer foi descartado para
  distribuição pública, pois exige certificado confiável ou etapa manual de
  confiança no PC. A solução usa somente .NET/Windows, GitHub Releases e a
  infraestrutura Cloudflare já existente.
- O pipeline de release gera o runtime ZIP separado, assina offline, verifica
  a chave pública incorporada, publica os artefatos e só então migra D1,
  implanta o Worker e troca o feed. A chave privada fica fora do repositório;
  a cópia local está criptografada fora do workspace e CI exige secrets.
- Eventos de manifesto, download, staging, ativação, saúde e rollback geram
  log JSONL local rotacionado. Com consentimento v3, somente o evento
  estruturado/sanitizado entra numa fila local limitada e idempotente, que
  reenvia após falha/rede offline para `POST /updater-events`; D1 e a área
  administrativa possuem a seção **Bugs do updater**.
- Validação local desta rodada: build Release sem avisos, 592 testes .NET,
  107 testes Worker, 36 testes dashboard, safety check, pacote self-contained,
  ZIP atômico e compilação/contrato do instalador. O smoke de instalação
  isolada não foi executado porque existe uma instalação real registrada e o
  script corretamente se recusou a sobrescrevê-la; Windows limpo continua
  sendo gate obrigatório da publicação oficial.

## Hardening do atualizador independente — 31/07/2026

- O handoff identifica o processo principal por PID e instante de criacao:
  se o Windows reutilizar o PID depois do fechamento, o updater não aguarda um
  processo alheio. O campo é obrigatório e coberto pelo parser testado.
- Durante hash e execucao, o updater retém um handle somente-leitura do setup,
  negando escrita/troca concorrente no mesmo caminho até o processo do Inno
  Setup iniciar e terminar. Build/testes Release e validação do pacote seguem
  obrigatórios antes de uma futura publicação.

## Atualizador independente e durável — 31/07/2026

- O fluxo de instalação da atualização foi separado do WPF: o novo projeto
  self-contained `FiveMCleaner.Updater` é incluído no payload, copiado para
  `%LOCALAPPDATA%\FiveMCleaner\Updater` antes do uso e executado fora da pasta
  que o Inno Setup substitui.
- O contrato entre app e atualizador é fechado: somente instalador sob
  `Updates`, tamanho, SHA-256, PID do app e log sob `Logs`. O atualizador
  revalida todos esses dados, espera o app sair sem encerramento forçado,
  executa o setup e exibe erro nativo com log em caso de falha. Testes cobrem o
  contrato e a cópia externa; a publicação permanece pendente de autorização
  explícita de release.

## Banner pós-atualização dispensável — 30/07/2026

- O aviso exibido após o instalador relançar o app com `--updated=X.Y.Z` agora
  inclui um botão X minimalista no canto superior direito do banner, permitindo
  fechar a confirmação sem ação adicional.
- `MainViewModel.DismissCompletedUpdateBanner()` limpa o estado
  `JustUpdatedToVersion` e oculta o banner; coberto por testes unitários.

## Hotfix do atualizador — 30/07/2026

- Identificado um impasse no fluxo de atualização silenciosa: o aplicativo
  aguardava quatro segundos pelo instalador, enquanto o Inno Setup podia
  aguardar o aplicativo liberar os arquivos instalados. Em PCs onde o
  Restart Manager detectava o bloqueio imediatamente, o setup encerrava com
  código 1. O app agora fecha assim que o processo verificado do instalador é
  criado, permitindo a substituição dos arquivos sem espera circular.
- A validação de caminho, extensão, origem, tamanho e SHA-256 permanece
  intacta. A pasta de logs continua preparada quando possível, mas falhas de
  diagnóstico não impedem a execução do instalador.

## Relatos de bug e dashboard publicado — 30/07/2026

- O caminho de ingestão foi validado de ponta a ponta até o D1: um relato
  sintético válido enviado para `POST /bugs` recebeu `202`, foi persistido
  com categoria, resumo, versão, perfil, ambiente e horário corretos, e foi
  removido após a prova. O código do app usa somente
  `CloudflareBugReportService`; não há endpoint, pacote ou chamada ativa do
  FormSubmit.
- O dashboard administrativo foi publicado com a versão que consulta e
  renderiza `GET /api/bugs`, inclusive sem exigir filtros de data ou versão.
  Relatos enviados ao Worker e persistidos no D1 agora ficam visíveis na
  seção **Bugs reportados** após autenticação.

## Validação final de telemetria — 30/07/2026

- O teste de produção enviou telemetria e relato de bug válidos; ambos
  receberam HTTP 202 e foram confirmados no D1 antes da limpeza dos dados
  sintéticos. Endpoints administrativos sem sessão responderam HTTP 401 e não
  aceitaram uma origem CORS não autorizada.
- URLs do dashboard foram removidas de documentação destinada ao repositório.
  A confidencialidade dos dados é garantida pela sessão administrativa do
  Worker; o endereço estático por si só não é um mecanismo de segurança.
- `SECURITY.md` foi corrigido para não afirmar incorretamente que o formulário
  ainda envia dados ao FormSubmit.

## Filtros do dashboard — 30/07/2026

- Sem data e sem versão, o dashboard publicado já carrega todos os eventos de
  Produção; a verificação visual confirmou a requisição somente com
  `environment=Production`. Informar versão acrescenta esse filtro às mesmas
  consultas e retornou o evento correspondente.
- Corrigido no Worker o limite final de data: `Até 30/07/2026` antes excluía
  eventos com horário naquele dia. Agora a consulta usa o início do dia
  seguinte como limite exclusivo, incluindo integralmente a data final. A
  mesma correção foi aplicada aos relatos de bugs e coberta por testes.
- A correção está somente no commit local até um deploy explicitamente
  autorizado; o dashboard publicado mantém o comportamento anterior para
  filtro de data até receber esse Worker.

## Prova ponta a ponta da telemetria em produção — 30/07/2026

- Teste externo completo do caminho real: um evento `Production` válido foi
  enviado para `POST /telemetry`, recebeu `202 Accepted`, foi localizado no
  D1 pelo conector oficial e apareceu no dashboard publicado com o filtro de
  Produção e versão exata: 1 otimização, 100% de sucesso e 1 s de duração.
- A tela de dashboard não apresentou erros de console. Todos os eventos
  sintéticos usados nesta e na validação anterior foram apagados com suas
  linhas auxiliares; a consulta final ao D1 confirmou zero remanescentes.
- Portanto, para instalações que tenham a correção de contrato `environment`
  e consentimento ativo, há evidência direta do percurso app/cliente ->
  Worker -> D1 -> dashboard. A versão pública antiga sem `environment` segue
  incompatível e deve receber o patch antes de ser considerada coberta.

## Validação pós-correção da telemetria — 30/07/2026

- Corrigidos dois problemas locais confirmados durante a revisão: tentativas de
  flush concorrentes podiam enviar o mesmo lote mais de uma vez; e eventos que
  já estavam na fila podiam ser enviados depois de o usuário retirar o
  consentimento. `QueuedCloudflareTelemetryService` agora serializa o flush e
  só transmite quando a telemetria continua autorizada.
- Cobertura de regressão: fila sem consentimento preserva o evento sem abrir
  conexão; dois flushes simultâneos fazem uma única requisição e removem a
  fila somente uma vez. A suíte .NET Release, a validação de segurança e os
  104 testes do Worker foram aprovados.
- Conexão externa validada novamente em `POST /telemetry`: o Worker ativo
  respondeu `202 Accepted` a um evento sintético marcado como `Development`.
  A consulta/limpeza direta no D1 permanece bloqueada nesta máquina pela
  ausência de `CLOUDFLARE_API_TOKEN`; não registrar token no repositório.

## Telemetria de produção — investigação de 30/07/2026

- A versão pública `1.1.0` tinha uma incompatibilidade de contrato: o Worker
  exige `environment`, porém o serializador .NET não incluía esse campo. O
  Worker respondia HTTP 400 e o cliente retinha o lote como falha transitória;
  por isso o dashboard permanecia vazio mesmo com consentimento válido.
- A correção em desenvolvimento serializa o ambiente real (`Development` ou
  `Production`), restringe configuração de produção ao host/rota Cloudflare
  autorizados e descarta rejeições HTTP 4xx permanentes. Falhas de rede, 429
  e 5xx continuam na fila local para tentativa futura.
- A validação remota comprovou HTTP 400 para o payload antigo e HTTP 202 para
  o mesmo evento com `environment: Production`. Consulta direta ao D1, logs e
  deploy do Worker e confirmação visual no dashboard dependem de credencial
  Cloudflare nesta sessão; não há prova completa até executar o checklist em
  `docs/telemetry-operations.md` com essa credencial.

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
  Imagens opcionais passam por sanitização antes do envio. Os nomes de campos
  enviados ao FormSubmit usam somente ASCII, embora os valores possam conter
  texto localizado: o provedor expõe nomes multipart com acentos como palavras
  MIME codificadas no e-mail, enquanto rótulos sem acentos preservam uma tabela
  legível para telemetria e relatos de bug.
- A telemetria técnica também é estritamente opcional e vem habilitada apenas
  em instalações novas; a pessoa pode desligá-la a qualquer momento. Quando
  habilitada, só envia por HTTPS uma allowlist de
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

- A distribuição pública atual é a versão `v1.0.3` e usa Inno Setup 6.7.3, com aplicativo e broker
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

## Publicação v1.0.3 (25/07/2026)

- A versão estável `v1.0.3` consolida todo o conteúdo da branch de
  desenvolvimento após a `v1.0.2`: diagnósticos e ajustes opt-in de FiveM/GTA
  V Legacy, benchmark standalone, telemetria documentada, licença
  source-available, confirmações seguras de interrupção e correções do motor
  transacional/broker.
- A linha pública continua usando o instalador self-contained `win-x64`, o
  manifesto com SHA-256 e o alias estável
  `FiveMCleaner-Setup-latest-win-x64.exe`. A página estática de download e o
  README exibem o resumo factual da `1.0.3`.
- A política de progressão pública do repositório exige a próxima versão
  sequencial após `1.0.2`; por isso esta publicação é `1.0.3`, validada por
  `scripts/Test-PublicVersionProgression.ps1`.

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

## Refinamento da central pública de download (25/07/2026)

- A página estática publicada pelo GitHub Pages em `website/public-site/` foi
  refinada sem alterar o aplicativo, instalador, manifesto de atualização ou a
  versão pública `1.0.3`.
- O CTA principal **Baixar para Windows** usa o símbolo Windows escuro com a
  perspectiva inclinada característica; **Ver no GitHub** usa a marca branca
  completa do GitHub, com área fixa e sem recorte lateral.
  Ambos mantêm rótulos em texto, foco visível e o download continua direto para
  o alias oficial do GitHub Releases.
- A seção **Última versão pública** foi movida para imediatamente após os três
  pilares de confiança (instalador, controle e atualizações), deixando o que
  mudou na release atual mais visível antes da apresentação detalhada do app.
- O refinamento visual adicional está isolado em `site-polish.css`; o teste
  renderizado verifica o carregamento dessa folha, os dois ícones e a nova
  ordem estrutural da seção. A publicação do GitHub Pages é permitida como
  atualização editorial independente, sem tag, release ou incremento de versão
  do aplicativo, quando autorizada explicitamente pelo usuário.

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

## Otimizações específicas do FiveM: cache, diagnóstico interno e instalação (24/07/2026)

- Trabalho local, **não publicado** (sem push da main/tag/release nesta
  etapa; apenas push de desenvolvimento de `dev/proxima-versao`, autorizado
  explicitamente pelo usuário nesta tarefa). Não altera versão pública,
  instalador nem site.
- Antes de implementar, o usuário foi consultado sobre três pontos que
  tensionavam decisões de segurança já documentadas: (1) remoção de
  `ros_id.dat`/`DigitalEntitlements` — proibida no modelo padrão; o usuário
  escolheu implementar com confirmação explícita a cada execução; (2)
  encerrar processos FiveM "abandonados" antes da limpeza — capacidade nova;
  o usuário escolheu permitir apenas para processos comprovadamente travados
  (`Process.Responding == false`); (3) reinstalação assistida/recriação
  completa de dados corrompidos — o usuário escolheu implementar a
  recriação automatizada dos dados regeneráveis. Todas as três decisões e
  suas justificativas de segurança estão documentadas em
  `docs/safety.md` (seções "Exceção documentada: reparo de dados de
  entitlement" e "Encerramento de processo travado").
- Seis novas ações no catálogo, catálogo avançou da versão 7 para a 8:
  - Três diagnósticos somente leitura, sempre presentes
    (`ActionOptionGate.Always`, nunca escrevem no sistema): tamanho e
    integridade do cache do FiveM por categoria com detecção de arquivos
    bloqueados (`CacheStorageDiagnosisAction`), saúde da instalação
    (instalação duplicada, permissão de escrita, sincronização por
    OneDrive, espaço livre em disco — `InstallationHealthDiagnosisAction`)
    e padrões recorrentes de erro/streaming a partir de nomes de crash
    dumps e do log mais recente (`CrashPatternDiagnosisAction`).
  - Três ações de reparo (🔧), cada uma com seu próprio `ActionOptionGate`
    dedicado, **desligadas por padrão e nunca incluídas em nenhum perfil
    automático** (Leve/Médio/Agressivo) — cobertas pelo teste
    `RepairActions_AreOptInAndNeverPartOfAnyDefaultProfile`:
    `StuckProcessTerminationAction` (encerra apenas um processo do FiveM
    comprovadamente sem resposta), `RecreateFiveMLocalDataAction` (reutiliza
    o padrão de quarentena já existente para recriar server-cache/
    server-cache-priv/logs/crashes, nunca as pastas protegidas por
    `docs/safety.md`) e `StaleAuthDataRepairAction` (só remove
    `ros_id.dat`/`DigitalEntitlements` quando um padrão de erro de
    entitlement é detectado no log; caso contrário não faz nada; sempre via
    quarentena reversível até a confirmação final da transação).
- Novo `src/FiveMCleaner.Windows/Infrastructure/StuckFiveMProcessInspector.cs`
  (`IStuckFiveMProcessInspector`): encontra e encerra apenas um processo
  cuja imagem pertence à instalação do FiveM e que não responde no momento
  da leitura; nunca um processo de terceiros, do GTA V ou do sistema.
- Novo `src/FiveMCleaner.Windows/Actions/FiveMInstallationActions.cs` com as
  seis ações acima e um helper interno `FiveMLogPatterns` compartilhado
  (busca textual honesta por códigos de erro/streaming/entitlement — nunca
  uma análise de despejo de memória).
- Itens do pedido original avaliados e **deliberadamente deferidos**, com
  justificativa (mesma disciplina das fases anteriores — não é lacuna
  silenciosa): detecção de arquivos bloqueados por antivírus (não há API
  segura e genérica para identificar qual AV bloqueou o quê); detecção de
  atalhos apontando para o executável errado (exigiria parsing de `.lnk`
  via COM, adiado); detecção de "arquivos estranhos injetados" na pasta
  (heurística com risco relevante de falso positivo); "preservar arquivos
  íntegros que seriam baixados novamente" (exigiria validar integridade por
  item de cache, não apenas por pasta); comparação de versão
  instalada vs. mais recente do FiveM (não há fonte oficial segura de
  "última versão" consultável sem rede, e o produto evita depender de rede
  para diagnóstico); monitoramento de CPU/GPU por NUI especificamente (não
  há quebra confiável de uso por subprocesso de NUI sem tocar a API do
  próprio FiveM); reinstalação automática do FiveM baixando/executando um
  instalador (permanece proibido por `docs/safety.md` — "sem baixar/
  executar código arbitrário" — independente da decisão sobre recriação de
  dados locais, que é uma operação diferente e mais restrita).
- Validação: `dotnet build` Release sem avisos/erros; **363 testes .NET
  aprovados** (358 anteriores + 5 novos: 3 ações opt-in nunca aparecem em
  nenhum perfil por padrão e aparecem corretamente quando habilitadas, mais
  a cobertura de localização/catálogo/plano já existente reajustada para a
  versão 8); `scripts\Verify-Safety.ps1` aprovado.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Presets gráficos ampliados: Qualidade, janela/VSync e recomendação (24/07/2026)

- Trabalho local, **não publicado** (sem push da main/tag/release nesta
  etapa; apenas push de desenvolvimento de `dev/proxima-versao`, autorizado
  explicitamente pelo usuário nesta tarefa). Não altera versão pública,
  instalador nem site.
- Antes de implementar, o usuário foi consultado sobre três tensões com o
  design de segurança já existente do motor gráfico (`LegacyGraphicsAction.
  cs`), que historicamente só **reduz** valores existentes: (1) o "Preset
  Qualidade" pedido exige **aumentar** opções — o usuário escolheu permitir
  aumento apenas nesse preset novo, mantendo Leve/Equilibrado/Agressivo
  somente-reduz; (2) resolução/tela cheia/janela/VSync/adaptador ficam numa
  parte do arquivo nunca tocada pelo app — o usuário escolheu implementar
  com validação, mas a implementação real ficou deliberadamente restrita a
  **janela e VSync** (dois booleanos sem risco de "tela preta"); resolução,
  taxa de atualização, adaptador de vídeo, proporção de tela, limite de FPS,
  escala de resolução e versão do DirectX ficaram de fora por exigirem
  validação contra os modos realmente suportados pelo monitor, que o app
  ainda não faz de ponta a ponta — ver `docs/safety.md` ("Escopo de edição
  gráfica"); (3) "configuração inteligente" — o usuário confirmou que deve
  ser apenas uma recomendação, nunca aplicada automaticamente; quem aplica
  continua sendo o usuário escolhendo manualmente.
- Catálogo avançou da versão 8 para a 9, com 6 novas ações:
  - `RecommendGraphicsPreset` (👁, sempre ativa): combina os diagnósticos já
    existentes de GPU/VRAM, CPU, RAM e monitor numa recomendação textual de
    qual preset (FPS/Equilibrado/Qualidade) combina com o hardware; nunca
    aplica nada sozinha. Heurística documentada e testada
    (`GraphicsPresetRecommendationAction.Recommend`), deliberadamente sem
    considerar servidor utilizado (sem API segura para identificá-lo) nem
    resultado de benchmark ainda não executado.
  - `DiagnoseTextureVramFit` (👁, sempre ativa): compara `TextureQuality`
    já configurado com a VRAM detectada da GPU usando um limiar
    conservador e documentado como estimativa, não medição real de uso.
  - `ApplyQualityLegacyGraphics`/`ApplyQualityGtaVGraphics` (🔧, opt-in,
    nunca em nenhum perfil automático): novo `GraphicsPresetDirection.
    RaiseOnly` em `LegacyGraphicsPresetAction`, reaproveitando 100% do
    mecanismo de backup/hash/troca atômica/rollback já existente. O preset
    `LegacyGraphicsPresets.Quality` eleva shadow/reflection/water/
    particles/grass/shader/postfx/tessellation/SSAO/anisotropic/texture/
    FXAA/densidade populacional/escala de distância até um teto
    conservador, deliberadamente sem tocar MSAA/ReflectionMSAA/TXAA
    (custo por GPU variável demais para adivinhar com segurança),
    distância/sombra estendida, motion blur ou profundidade de campo —
    para não descontrolar o 1% low, como pedido.
  - `ApplyLegacyDisplayPreferences`/`ApplyGtaVDisplayPreferences` (🔧,
    opt-in, nunca em nenhum perfil automático): nova
    `DisplayPreferencesAction`, mesmo padrão de segurança (backup/hash/
    troca atômica/rollback) da ação gráfica, mas restrita a `Windowed` e
    `VSync`. Corrigido durante a implementação um risco real: os arquivos
    de configuração do FiveM/GTA V Legacy não são consistentes no formato
    de booleano — alguns valores usam `"true"/"false"`, outros usam
    `"0"/"1"` (confirmado pelo próprio teste `GtaVGraphicsPresetTests`
    já existente, que usa `Windowed value="0"`); a ação lê ambos os
    formatos e **preserva o formato original** ao escrever, em vez de
    normalizar para `"true"/"false"` e arriscar quebrar leitura por outra
    ferramenta.
- Novo `src/FiveMCleaner.Windows/Actions/DisplayPreferencesAction.cs` e
  `GraphicsRecommendationActions.cs`. `LegacyGraphicsAction.cs` ganhou
  `LegacyGraphicsPresets.Quality`, `GraphicsPresetDirection` e um novo
  construtor que aceita `actionId`/`preset`/`direction` explícitos, mantendo
  o construtor por perfil (Leve/Médio/Agressivo) inalterado e sempre
  `LowerOnly`.
- Itens do pedido original avaliados e **já cobertos** pela implementação
  anterior (sem necessidade de novo código): resolução — não; mas
  qualidade de sombra/reflexo/água/partículas/grama/shader/pós-
  processamento/tesselação/oclusão de ambiente/filtragem anisotrópica/
  textura/densidade e variedade populacional/escala de distância (normal e
  estendida)/streaming detalhado durante voo/MSAA/Reflection MSAA/TXAA já
  existiam nos presets Leve/Equilibrado/Agressivo desde a implementação
  anterior do motor gráfico.
- Itens **deliberadamente deferidos**, com justificativa: resolução,
  frequência de atualização, adaptador de vídeo, proporção de tela (exigem
  validar a combinação contra os modos realmente suportados pelo monitor,
  o que o app não faz de ponta a ponta ainda); limite de FPS e escala de
  resolução (não há confirmação de que existam como chave estável no
  settings.xml do FiveM/GTA V Legacy nesta versão; preferimos não
  adivinhar uma chave e escrever um valor sem efeito ou, pior, incorreto);
  DirectX 10/10.1/11 (mesma razão: sem confirmação de uma chave estável e
  segura); sombras suaves (sem uma chave conhecida e testável distinta de
  `ShadowQuality`/`HighResolutionShadows` já cobertas).
- Validação: `dotnet build` Release sem avisos/erros; **375 testes .NET
  aprovados** (363 anteriores + 12 novos cobrindo o preset de Qualidade
  somente-eleva com rollback exato, a preservação de formato booleano de
  `DisplayPreferencesAction` — incluindo o caso `"0"/"1"` — recusa de
  escrita com o jogo aberto, a heurística de recomendação de preset nos
  três cenários e o diagnóstico de textura vs. VRAM); `scripts\
  Verify-Safety.ps1` aprovado.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Parâmetros de inicialização do GTA V standalone (24/07/2026)

- Trabalho local, **não publicado** (sem push da main/tag/release nesta
  etapa; apenas push de desenvolvimento de `dev/proxima-versao`, autorizado
  explicitamente pelo usuário nesta tarefa). Não altera versão pública,
  instalador nem site.
- Antes de implementar, o usuário foi consultado sobre um conflito direto
  com `docs/safety.md`: o item 🧪 pedido, `-disableHyperthreading`, é
  exatamente o que a seção "Ações proibidas" já veta explicitamente
  ("desliguem SMT/Hyper-Threading"). O usuário confirmou **não
  implementar**, mantendo a proibição sem exceção.
- Descoberta relevante ao avaliar o pedido: `docs/research.md` já
  documentava, com fonte no próprio código do FiveM
  (`BlockLoadSetters.cpp`), que o **FiveM bloqueia explicitamente a
  leitura do `commandline.txt`** do GTA — ou seja, escrever parâmetros
  nesse arquivo nunca teve efeito real para o FiveM. Isso confirma por que
  `docs/safety.md` já proibia editar `commandline.txt` "como otimização do
  FiveM": a proibição é sobre o *uso para FiveM*, não sobre o arquivo em
  si. A funcionalidade pedida foi implementada com escopo explícito e
  exclusivo do **GTA V Legacy standalone**, nunca do FiveM — reforçado em
  `docs/safety.md` (nova seção "Parâmetros de inicialização do GTA V
  standalone").
- Catálogo avançou da versão 9 para a 10, com 4 novas ações, todas restritas
  ao GTA V standalone (`environment.GtaVInstallationRoot`):
  - `DiagnoseGtaVLaunchParameters` (👁, sempre ativa): lê o commandline.txt
    existente e avisa especificamente quando um parâmetro de reparo
    (-safemode/-useMinimumSettings/-UseAutoSettings) ficou ativo além do
    necessário.
  - `ApplyGtaVGraphicsLaunchParameters` (opt-in, nunca em perfil
    automático): escreve -cityDensity/-anisotropicQualityLevel/-fxaa/
    -grassQuality/-lodScale/-frameLimit (este último usando a taxa de
    atualização já detectada do monitor, quando disponível, em vez de um
    valor arbitrário).
  - `ApplyGtaVDisplayLaunchParameters` (opt-in, nunca em perfil
    automático): escreve o modo de tela (-fullscreen/-windowed/
    -borderless, mutuamente exclusivos) e, quando escolhida, a versão do
    DirectX (-DX10/-DX10_1/-DX11). Nunca escreve -width/-height/adaptador,
    pela mesma razão de risco de modo não suportado já registrada na fase
    anterior de presets gráficos.
  - `ApplyGtaVRepairLaunchParameters` (opt-in, nunca em perfil automático):
    escreve -safemode/-useMinimumSettings/-UseAutoSettings individualmente;
    o aviso do plano (`gtav-repair-launch-parameters-are-temporary`) e o
    `undoSummary` da própria ação lembram explicitamente de reverter após
    diagnosticar, atendendo ao "🚫 Não deixar parâmetros de reparo ativos
    permanentemente" do pedido original.
- Novo `src/FiveMCleaner.Windows/Actions/GtaVLaunchParametersActions.cs`:
  um helper interno (`GtaVCommandLineFile`) faz parse/merge/escrita atômica
  do arquivo de texto plano, tocando somente as linhas cujo parâmetro
  pertence ao conjunto allowlisted de cada ação e preservando qualquer
  outra linha (inclusive parâmetros desconhecidos do produto) exatamente
  como estava — mesma filosofia de allowlist já usada nas ações gráficas
  em XML. Backup e rollback exato reaproveitam o mesmo padrão.
- `scripts/Verify-Safety.ps1` já continha uma verificação automática que
  bloqueia qualquer menção a `commandline.txt` no código-fonte (guarda
  literal da proibição antiga). Ela foi ajustada com um novo parâmetro
  `-ExcludeFileNames` em `Find-CSharpSourceMatches`, para permitir
  especificamente `GtaVLaunchParametersActions.cs` — um arquivo revisado,
  documentado e restrito ao GTA V standalone — sem enfraquecer a proteção
  para qualquer outro arquivo do projeto.
- Itens do pedido original avaliados e **deliberadamente deferidos**, com
  justificativa: `-disableHyperthreading` (decisão do usuário, mantém
  proibição de `docs/safety.md`); `-width`/`-height` (mesmo risco de modo
  de vídeo não suportado já documentado na fase de presets gráficos).
- Validação: `dotnet build` Release sem avisos/erros; **388 testes .NET
  aprovados** (375 anteriores + 13 novos cobrindo o diagnóstico de
  commandline.txt incluindo o aviso de parâmetro de reparo ativo, a
  mescla/preservação de linhas desconhecidas com rollback exato, a
  exclusividade mútua do modo de tela, a troca de versão do DirectX e a
  recusa de escrita com o GTA V aberto); `scripts\Verify-Safety.ps1`
  aprovado (após o ajuste de `-ExcludeFileNames` descrito acima).
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Investigação: fase administrativa falhando sem "Executar como administrador" (24/07/2026)

- Relato do usuário: um terceiro que testou a versão pública `v1.0.2` em
  duas máquinas diferentes, ambas no perfil Médio, teve a fase
  administrativa (ação `EnableSessionPerformancePowerPlan`, a única do
  catálogo com `RequiredPrivilege.Administrator`) falhar com "A fase
  administrativa falhou com segurança: O componente administrativo
  terminou sem uma confirmação válida." Rodando o app já **como
  administrador**, nenhuma ação falhou nas duas máquinas.
- Diagnóstico confirmado pelo código: essa mensagem específica
  (`ElevatedBrokerClient.cs`) só aparece quando o pipe nomeado local
  chegou a conectar com o broker elevado, mas o processo terminou **sem
  publicar nenhum evento terminal** (`Completed`/`Failed`/`Rejected`) —
  ou seja, o broker foi interrompido no meio da execução, não rejeitou
  nem falhou de forma limpa pelo próprio código dele.
- Hipótese mais bem sustentada (não confirmável sem logs de proteção da
  máquina do usuário, mas consistente com o próprio registro já existente
  em "Compatibilidade com antivírus" — "A versão sem assinatura
  Authenticode pode receber avisos de reputação/SmartScreen"): quando o
  app não está elevado, `Verb = "runas"` aciona o fluxo completo de
  elevação do Windows (`consent.exe`), que para um executável **sem
  assinatura digital** passa por checagem de reputação do
  SmartScreen/Defender — podendo interromper o broker no meio da
  execução. Quando o app já está elevado, essa checagem não é acionada
  de novo (não há nova elevação a fazer), o que explica por que o
  problema desaparece completamente nesse caso. A correção estrutural
  real é assinar digitalmente o app e o broker com um certificado
  Authenticode; isso é uma decisão de custo/infra, não uma mudança de
  código.
- Correções de código aplicadas nesta etapa, independentes da causa raiz
  acima (o usuário optou por corrigir o bug real de timeout e melhorar as
  mensagens, não só registrar o achado):
  - Bug real corrigido em `ElevatedBrokerClient.RunAsync`: o timeout de
    conexão de 30s (`ConnectionTimeout`) usava um `CancellationTokenSource`
    vinculado apenas ao token do timeout geral de 2 minutos
    (`OperationTimeout`), sem incluir o `cancellationToken` do chamador.
    Se os 30s expirassem antes dos 2 minutos, a exceção não batia com a
    cláusula `when (timeout.IsCancellationRequested)` do catch externo (só
    o token vinculado tinha sido cancelado, não o token externo) e escapava
    sem tratamento, virando um erro genérico em vez de uma mensagem
    utilizável. Corrigido isolando esse timeout em seu próprio try/catch,
    agora também vinculado ao `cancellationToken` do chamador, e
    convertendo em uma `TimeoutException` clara e específica.
  - Mensagem de fallback melhorada quando o broker conecta mas nunca
    publica um evento terminal (`DescribeMissingTerminalEvent`): usa o
    código de saída do processo para dar uma mensagem específica quando
    reconhecido (argumentos inválidos, falha de conexão do pipe, token não
    elevado) e, para um código de saída desconhecido — o caso mais comum
    de encerramento externo —, orienta explicitamente a verificar o
    histórico de proteção do Windows Defender/antivírus antes de tentar
    de novo, em vez do texto genérico anterior.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa (388
  testes) segue aprovada sem alteração de contagem — `ElevatedBrokerClient`
  spawna um processo elevado real e não tem cobertura automatizada
  (mesma limitação já registrada para a integração
  `AppOptimizationService → runtime real do Windows`); `scripts\
  Verify-Safety.ps1` aprovado.
- Próximo passo recomendado, fora do escopo desta tarefa: avaliar
  assinatura Authenticode do instalador/app/broker para eliminar a causa
  raiz suspeita, não só melhorar a mensagem de erro.

## Bug hunting em todo o aplicativo (24/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa até o momento
  do registro). Varredura sistemática por bugs reais em quatro áreas em
  paralelo: `FiveMCleaner.Core`/`Contracts` (catálogo, planejamento),
  `FiveMCleaner.Windows/Actions` (ações e infraestrutura), `FiveMCleaner.App`
  (ViewModel/serviços) e `FiveMCleaner.Broker` + motor transacional. Cada
  achado abaixo foi verificado manualmente antes da correção; nenhum é
  especulativo.
- **Bug real corrigido — gate de opção compartilhado incorretamente**
  (`ActionCatalog.cs`): `ApplyLegacyDisplayPreferences` (FiveM) e
  `ApplyGtaVDisplayPreferences` (GTA V standalone) usavam o mesmo
  `ActionOptionGate.ApplyDisplayPreferences`/mesmo flag em
  `OptimizationOptionsDto`, então habilitar o ajuste de janela/VSync do
  FiveM silenciosamente também planejava o do GTA V (e vice-versa),
  arrastando `VerifyGtaVIsStopped`/`RequiresGtaVStoppedFirst` como
  pré-requisito não solicitado. Corrigido com um gate e uma flag dedicados
  (`ActionOptionGate.ApplyGtaVDisplayPreferences` /
  `OptimizationOptionsDto.ApplyGtaVDisplayPreferences`), seguindo o mesmo
  padrão já usado para separar `ApplyLegacyGraphicsPreset` de
  `ApplyGtaVGraphicsPreset`. Teste de regressão adicionado
  (`DisplayPreferences_FiveMAndGtaVOptInsAreIndependent`); o teste
  existente que mascarava o bug (`GraphicsPresetsAndDisplayPreferences_
  AreOptInAndNeverPartOfAnyDefaultProfile`) foi corrigido para habilitar
  as duas flags explicitamente.
- **Bug real corrigido — journal preso em `Applying` após cancelamento no
  motor isolado** (`WindowsTransactionEngine.cs`, modo `IsolateFailures`,
  usado pelo fluxo padrão do app): quando o `cancellationToken` do usuário
  cancelava durante `ApplyAsync`/`CommitAsync` de uma ação, o código
  revertia só aquela ação e relançava a exceção sem nunca atualizar
  `journal.State` — que ficava travado em `Applying` permanentemente no
  arquivo persistido, porque só o fim normal do laço chamava
  `DetermineIsolatedFinalState`. Como `Applying` não é um dos estados que
  `ValidateExistingJournal` rejeita para retomada, uma chamada futura de
  `ExecuteAsync` com o mesmo ID de transação tentaria retomar esse journal
  e falharia com uma `InvalidOperationException` não tratada — travando a
  transação até exclusão manual do arquivo de journal. Corrigido com
  `FinalizeCancelledIsolatedRunAsync`: marca as ações ainda pendentes como
  `Skipped`/`NotRun`, calcula o estado final via a mesma
  `DetermineIsolatedFinalState` já usada no caminho normal, e persiste
  antes de relançar a exceção de cancelamento. Corrigida também uma
  inconsistência menor no mesmo arquivo (linha do "pré-requisito não
  atendido"): usava o `cancellationToken` do usuário em vez de
  `CancellationToken.None` para salvar o journal, ao contrário do padrão
  já usado no ramo "abortado" logo acima — um cancelamento naquele exato
  instante descartaria silenciosamente o estado `Skipped` calculado.
  Verificado que o teste de regressão novo
  (`Cancellation_LeavesJournalInATerminalStateInsteadOfStuckApplying`)
  falha sem a correção (`journal.State == Applying`) e passa com ela,
  antes de ser incluído na suíte.
- **Bugs reais corrigidos — inconsistência de cultura em números exibidos
  ao usuário**: `MainViewModel.FormatBytes` formatava bytes livres/
  baixados com a cultura ambiente da thread em vez de
  `localization.CurrentCulture` (o padrão já usado por todo o resto da
  ViewModel via `localization.Format`), podendo divergir do separador
  decimal do restante da mesma frase localizada quando a cultura do
  Windows diverge do idioma escolhido no app. Corrigido tornando o método
  de instância e usando `localization.CurrentCulture` explicitamente.
  Mesmo padrão de bug em `AppOptimizationService.GetMemoryModuleLayout`
  (composição "2×16 GB" de módulos de RAM); corrigido com
  `CultureInfo.InvariantCulture` explícito, já que esse identificador não
  deve variar por idioma.
- **Bug real corrigido — barra de progresso/ledger de etapas congelada
  durante a fase administrativa elevada** (`ElevatedBrokerClient.
  ReportBrokerProgress`): os eventos do broker elevado nunca populavam
  `AppProgressUpdate.Outcome`, então `MainViewModel.ApplyProgress` nunca
  chamava `UpsertStepLedgerItem` para a(s) ação(ões) administrativa(s) —
  o usuário não via confirmação de qual ação elevada foi aplicada/falhou
  no ledger ao vivo, só no relatório final. Corrigido mapeando
  `BrokerEventKindWire`/`Success` para `ActionExecutionOutcome` quando o
  evento tem um `ActionId`. `CompletedSteps`/`TotalSteps` da fase elevada
  foram deliberadamente deixados como estavam: o formato de evento do
  broker não expõe contagem de etapas por ação de forma confiável, e
  arriscar um número inventado seria pior do que a lacuna atual.
- Itens investigados e **descartados** (sem bug confirmado, para não
  inflar o relatório com nitpicks): toda a pasta
  `src/FiveMCleaner.Windows/Actions/` (padrões de backup/rollback,
  thresholds, lógica booleana de todas as ações revisadas linha a linha);
  `PlanValidator.cs` (nenhum campo do plano do cliente é confiado sem
  revalidação contra o plano canônico reconstruído); numeração de
  sequência do `NamedPipeEventWriter`; proteção contra clique duplo nos
  botões da ViewModel; disposal de `CancellationTokenSource` e
  desinscrição de eventos em `MainWindow.xaml.cs`.
- Validação: `dotnet build` Release sem avisos/erros; **390 testes .NET
  aprovados** (388 anteriores + 2 novos: independência dos opt-ins de
  janela/VSync do FiveM vs. GTA V, e o journal não ficar preso em
  `Applying` após cancelamento); `scripts\Verify-Safety.ps1` aprovado.

## Selo "recomendado" dinâmico e novos padrões de configuração (24/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa até o momento
  do registro).
- **Bug de UX corrigido**: o selo laranja "RECOMENDADO" na tela de visão
  geral e o texto do modo selecionado (`SelectedProfileLabel`) estavam
  **hardcoded no perfil Médio**, independente do que o app realmente
  diagnosticava como recomendado (`AppDiagnostic.RecommendedProfile`,
  calculado por `HardwareProfileAdvisor`). Corrigido em dois pontos:
  - `MainWindow.xaml`: o selo (antes só dentro do `RadioButton` do
    perfil Médio) agora existe nos três cartões (Leve/Médio/Agressivo),
    cada um com `Visibility` ligada a uma propriedade dedicada
    (`IsLightRecommended`/`IsBalancedRecommended`/`IsAggressiveRecommended`
    em `MainViewModel`), calculadas a partir do diagnóstico real.
  - `MainViewModel.SelectedProfileLabel`: antes sempre anexava
    " • RECOMENDADO" quando o perfil **selecionado** era Médio,
    independentemente do diagnóstico; agora anexa o selo apenas quando o
    perfil selecionado é de fato igual ao `RecommendedProfile` do
    diagnóstico, qualquer que seja ele.
  - A chave de recurso `Profiles.Balanced.Badge` (texto "RECOMENDADO",
    fixa ao perfil Médio) foi renomeada para `Profiles.RecommendedBadge`
    em `Strings.resx`/`Strings.pt-BR.resx`, já que o texto nunca foi
    específico do perfil — só o binding condicional era o problema.
- **Padrões de configuração alterados a pedido do usuário**
  (`AppSettings` em `AppModels.cs`):
  - `MinimizeToTrayOnClose` agora tem padrão `true` (antes `false`):
    instalações novas iniciam com "Minimizar para a bandeja" habilitado.
  - `ShareAnonymousTelemetry` agora tem padrão `true` (antes `false`):
    instalações novas iniciam com "Ajude a melhorar o FiveMCleaner"
    habilitado. A telemetria continua estritamente allowlisted (tipo de
    evento, duração, versão, categoria de erro fechada) e nunca lê
    logs/arquivos/documentos/histórico/caminhos/hardware/dados pessoais —
    só o padrão de ativação mudou, não o escopo do que é coletado. O
    usuário continua podendo desativar a qualquer momento nas
    configurações, e uma configuração já salva anteriormente com o toggle
    explicitamente desligado é preservada (o novo padrão só vale quando o
    arquivo de configuração ainda não define esse valor, ou seja,
    instalação nova). `docs/safety.md` e `docs/telemetry.md` atualizados
    para refletir o novo padrão sem enfraquecer a documentação do escopo
    de dados.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa (390
  testes) segue aprovada, com um teste de deserialização de configuração
  antiga (`ExistingSettingsJson_DefaultsToAutomaticLanguage`) ajustado
  para refletir o novo padrão de telemetria; `scripts\Verify-Safety.ps1`
  aprovado. Não há teste automatizado dedicado para `MainViewModel`
  (mesma lacuna já registrada para a camada de app/ViewModel).
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Notificação nativa do Windows para atualização disponível (24/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa até o momento
  do registro).
- Reaproveitado o mecanismo já existente de bandeja
  (`TrayIconService`/`System.Windows.Forms.NotifyIcon`) em vez de
  introduzir uma API de toast WinRT nova: `NotifyIcon.ShowBalloonTip` já
  renderiza como notificação nativa do Central de Ações do Windows
  10/11, usando o ícone do próprio app (`notifyIcon.Icon`, já extraído do
  executável) e o nome do app (`notifyIcon.Text = "FiveMCleaner"`) — sem
  dependência nova.
- `MainViewModel.CheckForUpdatesAsync` agora dispara um novo evento
  `UpdateAvailableDetected` (com a versão nova como string) exatamente no
  momento em que uma atualização é detectada, além do já existente
  `AddLog`. `MainWindow` assina esse evento e chama
  `TrayIconService.ShowUpdateAvailable(version)`.
- `TrayIconService.ShowUpdateAvailable`: se o ícone da bandeja já não
  estiver visível (app em primeiro plano, sem "minimizar para a bandeja"
  ativo), ele é tornado visível só para carregar a notificação e volta a
  ficar oculto ~8s depois, para não deixar um ícone de bandeja indesejado
  para quem nunca ativou essa preferência. Se o app já estiver minimizado
  para a bandeja, a notificação usa o ícone já visível normalmente.
- Clicar na notificação (`NotifyIcon.BalloonTipClicked`, reaproveitando o
  mesmo evento `ShowRequested` já usado pelo clique duplo no ícone da
  bandeja) restaura e ativa a janela principal, igual ao fluxo já
  existente de "voltar da bandeja".
- Novas chaves de recurso `Notification.UpdateAvailable.Title`/`.Message`
  em `Strings.resx`/`Strings.pt-BR.resx`, seguindo o mesmo padrão visual
  já usado no banner de atualização dentro do app.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa (390
  testes) segue aprovada sem alteração de contagem — a notificação nativa
  do Windows não tem cobertura automatizada (mesma lacuna já registrada
  para a camada de app/ViewModel; verificação depende de execução manual
  em uma máquina Windows real). `scripts\Verify-Safety.ps1` aprovado.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Fundação do novo modelo de consentimento de privacidade (25/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa). Não altera
  versão pública, instalador nem site. Primeiro incremento, deliberadamente
  restrito, de um plano maior (telemetria central via Cloudflare
  Workers/D1 + relatório de falhas via Sentry) discutido e revisado com o
  usuário nesta mesma etapa, mas ainda não implementado além do que está
  descrito abaixo.
- Decisão de produto confirmada pelo usuário: tanto `ShareAnonymousTelemetry`
  quanto o novo `ShareCrashReports` nascem `true` em instalações novas (fase
  inicial do produto, onde esses dados são valiosos para identificar falhas
  reais), mas **nenhum envio pode ocorrer sem confirmação explícita** numa
  tela de consentimento futura — daí a necessidade de um terceiro campo,
  `PrivacyConsentVersion` (`int?`, nasce `null`), que funciona como o
  verdadeiro portão de autorização, independente do valor dos booleanos.
- Novo `src/FiveMCleaner.App/Services/PrivacyConsentPolicy.cs`: fonte única da
  versão atual de consentimento (`CurrentVersion = 1`) e do histórico
  descritivo de cada versão (`History`), sem qualquer dependência de UI,
  disco, rede, Cloudflare ou Sentry — puramente declarativo, para poder ser
  reutilizado tanto pela futura tela de consentimento quanto pelo avaliador.
- Novo `src/FiveMCleaner.App/Services/PrivacyConsentEvaluator.cs`: lógica pura
  (`PrivacyConsentEvaluator.Evaluate(AppSettings, bool settingsFileExistedBeforeLoad)`)
  que decide, a partir de um `AppSettings` já carregado, se a tela de
  consentimento precisa aparecer, qual variante (primeira instalação,
  atualização de instalação antiga, renovação de versão, ou já válida) e se
  cada tipo de envio (telemetria de uso / relatório de falhas) está
  autorizado. Autorização exige simultaneamente o booleano correspondente
  `true` **e** `PrivacyConsentVersion >= PrivacyConsentPolicy.CurrentVersion`;
  nenhum dos dois sozinho autoriza envio. Sem acesso a disco: quem carrega
  `AppSettings` e decide se o arquivo já existia continua sendo a camada de
  serviço existente (`AppOptimizationService.LoadSettingsAsync`), preservando
  a composição manual do projeto (nenhum container de DI foi adicionado).
- `AppSettings` ([AppModels.cs](src/FiveMCleaner.App/Services/AppModels.cs))
  ganhou `ShareCrashReports` (`true` por padrão) e `PrivacyConsentVersion`
  (`int?`, `null` por padrão). Compatibilidade com `settings.json` antigos
  verificada e testada: um arquivo salvo por uma versão anterior do app (só
  com `shareAnonymousTelemetry`) preserva esse valor exatamente como estava
  (aceito ou recusado) e ganha `shareCrashReports = true`/
  `privacyConsentVersion = null` apenas como default do campo ausente — o
  avaliador trata `PrivacyConsentVersion == null` como "ainda não decidiu",
  então nenhum envio de telemetria ou crash report é autorizado até uma
  futura tela de consentimento confirmar isso explicitamente, mesmo com os
  booleanos em `true`.
- **Não implementado nesta etapa, por instrução explícita do usuário**: a
  tela de consentimento em si, qualquer alteração visual, o transporte HTTP,
  a fila local resiliente, o Worker Cloudflare, D1, Sentry, handlers de
  exceção não tratada, e qualquer remoção/alteração do `FormSubmit` atual
  (`FormSubmitAnonymousTelemetryService` continua exatamente como estava).
  Nenhuma operação remota foi realizada.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa foi de
  390 para **411 testes aprovados** (21 novos cobrindo
  `PrivacyConsentPolicy`, os quatro cenários de tela do
  `PrivacyConsentEvaluator`, as combinações de autorização por tipo de envio,
  e a (de)serialização de `AppSettings` — incluindo um `settings.json` antigo
  fixture real, com e sem telemetria aceita); `scripts\Verify-Safety.ps1`
  aprovado.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Tela de consentimento de privacidade e integração no startup (25/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa). Não altera
  versão pública, instalador nem site. Segundo incremento do plano de
  telemetria central, sobre a fundação (`PrivacyConsentPolicy`/
  `PrivacyConsentEvaluator`/campos de `AppSettings`) registrada na entrada
  anterior.
- Nova janela WPF `PrivacyConsentWindow`
  ([PrivacyConsentWindow.xaml](src/FiveMCleaner.App/Views/PrivacyConsentWindow.xaml)/
  `.xaml.cs`), no mesmo padrão visual/arquitetural de `BugReportWindow`/
  `OptimizationConfirmationWindow` (chrome customizado, sem DI). Mostra
  título e introdução variando por cenário (primeira instalação, upgrade de
  instalação antiga, renovação de versão), dois toggles independentes
  pré-marcados conforme `AppSettings` atual, e as seções "Coletamos"/"Não
  coletamos" com a lista completa de campos pedida. Fechar pela X ou
  Alt+F4 é tratado pelo mesmo caminho de "Continuar", só que com os dois
  valores como recusados — nunca impede o app de abrir.
- Nova lógica pura `PrivacyConsentOutcomeBuilder`
  ([PrivacyConsentOutcomeBuilder.cs](src/FiveMCleaner.App/Services/PrivacyConsentOutcomeBuilder.cs)):
  transforma a escolha do usuário (ou o fechamento da janela) no
  `AppSettings` a persistir, sempre preservando os demais campos e
  carimbando `PrivacyConsentVersion = PrivacyConsentPolicy.CurrentVersion`.
  Sem UI/disco/rede — testável isoladamente.
- Ponto de integração escolhido: `MainViewModel.InitializeAsync` (não
  `App.xaml.cs` nem código disperso em `MainWindow`) calcula a decisão do
  `PrivacyConsentEvaluator` logo depois de aplicar as settings já
  carregadas, expondo-a como a propriedade `PrivacyConsentDecision`. Um
  novo `IAppOptimizationService.SettingsFileExists()` (checagem de
  existência de arquivo, sem reanalisar o conteúdo) evita duplicar a
  leitura de `settings.json` só para distinguir instalação nova de upgrade
  antigo. `MainWindow.xaml.cs` só decide **quando** abrir a janela (mesmo
  padrão já usado para `BugReportWindow`) e chama o novo
  `MainViewModel.ConfirmPrivacyConsentAsync`, que reaproveita o mecanismo
  de persistência já existente (`BuildSettingsSnapshot`/
  `SaveSettingsRevisionAsync`) — nenhum segundo sistema de gravação foi
  criado. A janela é exibida via `ShowDialog()` com `Owner = MainWindow`
  logo após `InitializeAsync`, antes de qualquer outra interação (inclusive
  antes de minimizar para a bandeja em `--startup`), bloqueando a janela
  principal enquanto pendente; modo demo (`--demo`/`--demo-synthetic`)
  nunca mostra a tela, para não travar smoke tests automatizados.
- Nova seção **Privacidade** nas Configurações: card dedicado com os dois
  toggles (`ShareAnonymousTelemetry`, já existente, e o novo
  `ShareCrashReports`) e o mesmo resumo de "Coletamos"/"Não coletamos".
  Alterar os toggles ali persiste imediatamente pelo `SettingsChanged` já
  existente, mas nunca toca `PrivacyConsentVersion` nem reabre a tela
  inicial — `BuildSettingsSnapshot` lê a versão do campo interno, não
  recalculada pelos toggles.
- Novas chaves de localização (`PrivacyConsent.*`, `Privacy.Collects.*`,
  `Privacy.DoesNotCollect.*`, `Settings.Privacy.*`, `Settings.CrashReports.*`)
  em `Strings.resx`/`Strings.pt-BR.resx`, cobertas pelo teste existente de
  contrato de localização (agora também escaneando
  `PrivacyConsentWindow.xaml`/`.xaml.cs`).
- **Não implementado nesta etapa, por instrução explícita do usuário**:
  Sentry, Cloudflare Worker, D1, fila local resiliente, transporte HTTP em
  lote, e qualquer remoção/alteração do `FormSubmit` atual. Nenhuma
  operação remota foi realizada.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa foi de
  411 para **430 testes aprovados** (19 novos cobrindo
  `PrivacyConsentOutcomeBuilder` — as quatro combinações de aceite/recusa,
  fechamento como recusa equivalente, preservação de outras configurações
  — e a integração `MainViewModel` com um novo double
  `FakeAppOptimizationService`/`RecordingTelemetryService`: decisão correta
  nos quatro cenários, estado inicial dos checkboxes refletindo valores
  antigos, persistência correta da versão de consentimento, nenhum evento
  de telemetria disparado só por confirmar/recusar consentimento, e
  configurações não relacionadas preservadas); `scripts\Verify-Safety.ps1`
  aprovado.
- Nenhum arquivo do site ou do instalador foi tocado nesta etapa; não houve
  necessidade de revalidar `website/`.

## Relatório de falhas via Sentry, configuração centralizada e scaffold do Worker Cloudflare/D1 (25/07/2026)

- Trabalho local, **não publicado** (nenhum push nesta etapa). Não altera
  versão pública, instalador nem site. Terceiro incremento do plano de
  telemetria central, sobre o consentimento versionado e a tela já
  implementados nas duas etapas anteriores.
- O usuário forneceu o DSN do Sentry e o nome/ID do banco D1 do Cloudflare,
  pedindo explicitamente para não deixar esses valores fixos no código, usar
  configuração centralizada por ambiente (Development/Production) e não
  fazer nenhum deploy sem autorização. Antes de implementar, o escopo exato
  foi confirmado com o usuário: (1) integração real do SDK Sentry agora
  (não só a configuração) e (2) criação do scaffold do Worker/D1 agora
  (sem implantar).
- **Configuração centralizada** (`src/FiveMCleaner.App/Config/`):
  `appsettings.json` (base/fallback seguro, sem DSN),
  `appsettings.Development.json` e `appsettings.Production.json` (mesmo DSN
  nos dois — único projeto Sentry —, diferindo apenas no campo
  `environment`). Novo `AppEnvironment.Resolve()` decide qual arquivo usar:
  variável de ambiente `FIVEMCLEANER_ENVIRONMENT` tem prioridade máxima
  (definida como `Development` por `scripts/Start-DevelopmentApp.ps1`); sem
  ela, build Debug resolve `Development` e build Release resolve
  `Production` — a distribuição pública real é sempre Release e nunca
  define a variável, então nunca precisa de nada especial para cair em
  Production. Novo `RemoteServicesOptionsLoader` lê o arquivo
  correspondente com o mesmo `FiveMCleanerJson.Options` já usado em todo o
  app; falha ao ler (arquivo ausente, JSON malformado) sempre cai num
  fallback seguro sem DSN, nunca lança exceção.
- **Integração real do Sentry** (`FiveMCleaner.App` apenas — pacote NuGet
  `Sentry 6.7.0`, nunca referenciado por `Core`/`Windows`/`Broker`):
  `ICrashReportingService`/`SentryCrashReportingService`/
  `NoOpCrashReportingService`, com um holder estático `CrashReporting.Current`
  (mesmo padrão já usado por `LocalizationService.Current`) para que os
  handlers estáticos de `App.xaml.cs` tenham sempre um alvo seguro. Nunca
  inicializado antes do consentimento: `MainWindow.InitializeCrashReportingIfAuthorized`
  roda só depois que `ShowPrivacyConsentIfNeededAsync` resolve, verificando
  `viewModel.ShareCrashReports` (já garantidamente com
  `PrivacyConsentVersion` em dia nesse ponto). `App.xaml.cs` ganhou os
  handlers que faltavam desde a investigação anterior de bug hunting —
  `AppDomain.CurrentDomain.UnhandledException` e
  `TaskScheduler.UnobservedTaskException` — além do já existente
  `DispatcherUnhandledException`, todos roteando para
  `CrashReporting.Current.CaptureException` de forma best-effort (uma falha
  do Sentry nunca mascara o crash original nem lança de dentro do handler).
  `SentryOptions` desliga explicitamente tudo que não seja o evento de erro
  sanitizado: `SendDefaultPii=false`, `IsEnvironmentUser=false`,
  `AutoSessionTracking=false`, `CaptureFailedRequests=false`,
  `TracesSampleRate=0`. Novo `CrashReportSanitizer` roda em todo evento via
  `BeforeSend`, reaproveitando o mesmo `ReportSanitizer` já usado no
  relatório técnico para substituir caminhos pessoais, além de sempre
  sobrescrever `ServerName` para um valor fixo não identificável e limpar
  `User.Id/Username/Email/IpAddress`.
- **Scaffold do Worker Cloudflare/D1** (`infra/cloudflare-worker/`, **não
  implantado**, sem wiring do cliente .NET ainda): `wrangler.toml` com
  seções `env.development`/`env.production` (ambas apontando para o mesmo
  banco D1 fornecido, já que só um foi provisionado; linhas distinguidas
  por uma coluna `environment`), `schema.sql` com a tabela
  `telemetry_events` espelhando o allowlist já documentado, `src/index.js`
  (Worker mínimo) e `src/validateEvent.js` (validação pura, testada com o
  test runner nativo do Node — 16 testes, sem precisar de Miniflare/
  wrangler). `README.md` próprio documenta o que falta (deploy, migração
  remota do schema, wiring do cliente) como pendências futuras que exigem
  autorização explícita antes de qualquer operação remota.
- Durante a verificação de `git status`, apareceu um `.wrangler/cache/
  wrangler-account.json` na raiz do repositório contendo o e-mail e o ID da
  conta Cloudflare real do usuário — não foi gerado por nenhum comando
  executado nesta tarefa (nenhum `wrangler` foi chamado). Adicionado a
  `.gitignore` (`/.wrangler/`) e nunca staged/commitado; registrado aqui
  para transparência, já que é um artefato inesperado do ambiente, não do
  código desta etapa.
- Documentação atualizada: `docs/telemetry.md` (nova seção "Relatório de
  falhas (Sentry)" com tabela de campos, configuração centralizada por
  ambiente e nota sobre o Worker/D1 futuro), `docs/architecture.md` (nova
  subseção "Relatório de falhas e configuração centralizada") e
  `docs/safety.md` (terceira exceção de privacidade, mesmas garantias de
  nunca rodar no broker e nunca hardcoded).
- Validação: `dotnet build` Release sem avisos/erros; suíte completa foi de
  430 para **450 testes aprovados** (20 novos: resolução de ambiente com e
  sem variável, carregamento de configuração por ambiente com fallback
  seguro em arquivo ausente/malformado, o holder estático
  `CrashReporting`/`NoOpCrashReportingService`, sanitização real de
  `SentryEvent` — nome de servidor, dados de usuário e caminho pessoal na
  mensagem — e dois testes de guarda que escaneiam todo o código-fonte do
  app garantindo que o DSN do Sentry e o ID do banco D1 nunca aparecem fora
  dos arquivos de configuração/infraestrutura corretos);
  `scripts\Verify-Safety.ps1` aprovado. Os 16 testes puros do Worker
  (`npm test` em `infra/cloudflare-worker`) passam fora do pipeline .NET,
  sem depender de Miniflare/wrangler.
- Pendências explícitas para etapas futuras: nenhuma fila local, transporte
  HTTP em lote ou remoção do FormSubmit foi implementada — a telemetria de
  uso continua exatamente como estava; o Worker/D1 não foi implantado nem
  teve schema aplicado remotamente; nenhuma operação remota (deploy,
  publicação, `wrangler` real) foi executada nesta etapa.

## Expansão da telemetria (hardware/ações), scaffold do painel administrativo e auth própria (25/07/2026)

- Trabalho local, **não publicado** (nenhum push/deploy nesta etapa).
  Quarto incremento do plano de telemetria central. Antes de implementar, o
  escopo foi confirmado com o usuário em duas decisões: (1) expandir a
  coleta de telemetria agora (hardware/OS/ações), o que exige subir
  `PrivacyConsentPolicy.CurrentVersion` e renovar o consentimento de todos;
  (2) autenticação própria do painel (senha + sessão + proteção contra
  força bruta), sem domínio próprio, sem Cloudflare Access e sem OAuth
  Google/GitHub.
- **Consentimento renovado para a versão 2**: `PrivacyConsentPolicy.
  CurrentVersion` avançou de 1 para 2, com entrada no histórico explicando
  a mudança. A tela de consentimento e a seção Privacidade de Configurações
  ganharam dois novos itens em "Coletamos": modelo de CPU/GPU (sem número
  de série) e faixa aproximada de RAM — nas duas línguas. Qualquer
  instalação que já tinha aceitado a versão 1 verá a tela de renovação no
  próximo lançamento, mesmo sem alterar nada na tela em si (comportamento já
  coberto pelos testes de `PrivacyConsentEvaluator` da segunda etapa).
- **`AnonymousTelemetryEvent` expandido** (compatível com o contrato
  anterior — todos os campos novos são opcionais e no final):
  `OsVersion`, `SystemArchitecture`, `CpuModel`, `GpuModel`,
  `RamBucketGiB` (faixa fixa: 2/4/8/16/32/64/128/256, sempre arredondada
  para cima por `RamBucketCalculator`, nunca o valor exato), `Profile` e
  `ActionIds` (IDs das ações do plano aplicado). `MainViewModel.
  TrackOptimizationTelemetry` popula esses campos a partir do diagnóstico e
  do plano já carregados — nenhuma leitura nova de hardware foi criada, só
  reaproveitamento de `AppDiagnostic.CpuName/GpuName/TotalMemoryGiB/
  OsLabel/SystemArchitecture` já existentes. O transporte ativo hoje
  (`FormSubmitAnonymousTelemetryService`) continua enviando só os quatro
  campos originais, inalterado — os campos novos só são de fato
  transmitidos pelo transporte Cloudflare, ainda inativo.
- **Transporte Cloudflare completo, porém inativo** (`CloudflareTelemetryService.cs`):
  `TelemetryEventValidator` (validação completa do novo esquema),
  `LocalTelemetryQueue` (persistência atômica em
  `%LOCALAPPDATA%\FiveMCleaner\Telemetry\pending`, purga após 14 dias),
  `CloudflareTelemetryTransport` (POST HTTPS em lote, nunca lança) e
  `QueuedCloudflareTelemetryService` (implementa `IAnonymousTelemetryService`,
  enfileira e tenta flush best-effort a cada `TrackAsync` e uma vez no
  startup). `MainWindow` escolhe entre FormSubmit e Cloudflare com base em
  `RemoteServicesOptions.TelemetryEndpoint` (novo campo de config, `null`
  em ambos os arquivos de ambiente hoje) — **nunca os dois ativos ao mesmo
  tempo**, por construção. `RemoteServicesOptions` agora é carregado uma
  única vez no construtor de `MainWindow` (reaproveitado depois pela
  inicialização do Sentry), evitando duplicar a leitura de config.
- **Scaffold completo do Worker Cloudflare** (`infra/cloudflare-worker/`,
  **não implantado**): schema D1 expandido (`telemetry_events` com as
  colunas novas, `telemetry_event_actions` normalizada para "função mais
  usada", `login_attempts` e `admin_sessions` para a autenticação do
  painel); `validateEvent.js` espelhando o validador .NET; endpoints REST
  de estatística (`src/stats/queries.js`, uma função pura por gráfico —
  otimizações por dia, versões de Windows/app, funções mais usadas, tempo
  médio, taxa de sucesso, erros por versão, CPU/GPU/RAM mais comuns, perfis
  escolhidos — todas parametrizadas, nunca por interpolação de string, e
  filtradas por `environment='Production'` por padrão para não misturar
  testes do desenvolvedor com dados reais) e `src/stats/csv.js` (exportação
  CSV pura).
- **Autenticação própria do painel** (`src/auth/`): senha nunca em texto
  puro — hash PBKDF2-SHA256 (210 mil iterações, Web Crypto nativo, sem
  dependência de terceiros) gerado localmente por
  `scripts/hash-admin-password.mjs` e guardado só como Secret do Worker
  (`ADMIN_PASSWORD_HASH`); proteção contra força bruta por IP (`login_attempts`,
  HMAC do IP com o Secret `IP_HASH_SECRET`, nunca o IP em claro, 5
  tentativas por 15 minutos); sessões revogáveis do lado do servidor
  (`admin_sessions`, cookie `HttpOnly`/`Secure`/`SameSite=Strict` carregando
  só um ID aleatório de 256 bits — logout de verdade, não apenas expiração).
  `src/auth/passwordAuthProvider.js` expõe só três funções
  (`login`/`logout`/`requireSession`) para que um provedor futuro (OAuth,
  Cloudflare Access) possa substituí-lo sem tocar nas rotas.
- **Scaffold do painel** (`infra/dashboard/`, **não implantado**): site
  estático (HTML/CSS/JS puro, sem framework nem build step, pronto para
  Cloudflare Pages) com tela de login, filtros (período/versão), tiles
  (otimizações no período, taxa de sucesso, tempo médio) e gráficos em
  canvas (linha/barra) para cada estatística do Worker, mais exportação
  CSV por gráfico. Lógica de formatação/agregação (`assets/charts.js`,
  `assets/api.js`) extraída em módulos puros e testados; a renderização em
  canvas (`assets/rendering.js`) e a colagem de DOM (`assets/app.js`) não
  são cobertas por teste automatizado (exigiria um polyfill de canvas/DOM,
  fora do escopo desta etapa) — registrado honestamente no README do
  painel. O rodapé do painel deixa explícito que "otimizações" conta
  eventos, não usuários únicos, já que a telemetria nunca carrega
  identificador de máquina.
- Documentação atualizada: `docs/telemetry.md` (tabela de campos v2,
  seção do Worker/painel expandida), `docs/architecture.md` e
  `docs/safety.md` (corrigidas duas frases desatualizadas de etapas
  anteriores que ainda diziam "nasce como false"/"nunca hardware" —
  inconsistentes com o que já estava implementado; e nova nota sobre a
  autenticação do painel).
- Validação: `dotnet build` Release sem avisos/erros; suíte .NET foi de 450
  para **486 testes aprovados** (36 novos: `RamBucketCalculator`,
  `TelemetryEventValidator`, `LocalTelemetryQueue`, `CloudflareTelemetryTransport`,
  `QueuedCloudflareTelemetryService`); `scripts\Verify-Safety.ps1` aprovado.
  Worker: **67 testes** via `npm test` em `infra/cloudflare-worker`
  (validação estendida, `crypto.js`, `bruteForceGuard.js`, `sessionStore.js`,
  `stats/queries.js`, `stats/csv.js` — tudo puro, sem Miniflare/wrangler).
  Painel: **22 testes** via `npm test` em `infra/dashboard`
  (`assets/api.js`, `assets/charts.js`). Total de 575 testes automatizados
  entre os três projetos.
- Pendências e lacunas conhecidas, registradas honestamente: a glue
  D1-touching de `passwordAuthProvider.js` e o roteamento de `index.js` do
  Worker não têm teste automatizado (exigiriam Miniflare); a renderização
  em canvas do painel não tem teste automatizado; nenhum deploy, migração
  remota de schema ou secret foi configurado; o cliente .NET continua
  enviando telemetria só pelo FormSubmit (o transporte Cloudflare existe,
  mas fica inativo até um endpoint real ser configurado); o painel de
  "crash management" tipo centro-de-comando descrito pelo usuário
  (clicar no erro, ver linha, contagem de usuários, marcar como
  resolvido, regressão) é, na prática, a própria interface web do Sentry
  (sentry.io) — não foi reconstruído dentro do painel próprio, já que
  seria duplicar um produto que o Sentry já entrega nativamente assim que
  o DSN estiver configurado e enviando eventos reais.

## Publicação real do Worker e do painel (Development/Production compartilhando um único deploy) (25-26/07/2026)

- Publicação real autorizada explicitamente pelo usuário nesta etapa
  ("conclua e publique de fato com deploy, deixe tudo funcional"). Antes de
  qualquer comando remoto, o impacto de cada um foi explicado (schema no D1
  real, deploy do Worker, deploy do Pages), conforme pedido.
- **Correção pendente da etapa anterior**: `appsettings.Development.json`
  já estava com `telemetryEndpoint: null` no repositório — o valor de teste
  do passo 8 do guia de testes locais nunca chegou a ser commitado, então
  não havia nada para reverter. Também foi removida uma pasta vazia
  (`infra/cloudflare-worker/infra/`) que sobrou de um erro de `cd` numa
  sessão anterior — sem arquivos, apenas diretórios soltos.
- **Painel aprimorado**: logo do FiveMCleaner (login e cabeçalho, mesmo
  ícone de `website/public-site/icon.png`), gráficos organizados em seções
  (Adoção/Hardware/Diagnóstico de bugs), filtro de Ambiente (Produção/
  Desenvolvimento/Todos — `environment=All` agora ignora o filtro de
  ambiente na query em vez de comparar com um valor mágico), tile de
  "Falhas no período" e uma tabela de "Últimos erros" não agregada.
- **Três novas estatísticas no Worker**, pensadas especificamente para achar
  a origem de bugs sem precisar esperar volume suficiente para aparecer nos
  gráficos agregados: `error-categories` (erros por categoria, todas as
  versões juntas), `top-actions-in-failures` (quais ações mais aparecem em
  falhas especificamente) e `recent-failures` (feed cru dos últimos erros,
  com hardware/versão/ambiente). 13 novos testes cobrindo essas queries e a
  lógica pura nova do painel.
- **Publicação real, passo a passo**:
  1. Confirmado que o `wrangler` já estava autenticado nesta máquina na
     conta real do usuário (`felipemarquesini10@gmail.com`).
  2. Schema aplicado no D1 **remoto** (`--remote`): 4 tabelas criadas,
     idempotente, sem dado prévio para perder (primeira migração).
  3. A conta não tinha subdomínio `workers.dev` registrado ainda — como essa
     escolha define a URL de todos os Workers futuros da conta, a decisão
     foi devolvida ao usuário em vez de decidida automaticamente; ele
     registrou `felipemarquesini10.workers.dev` pelo painel da Cloudflare.
  4. Worker publicado: `https://fivemcleaner-telemetry.felipemarquesini10.workers.dev`.
  5. Secrets `ADMIN_PASSWORD_HASH` e `IP_HASH_SECRET` configurados via
     `wrangler secret put` (nunca no repositório). A senha de administrador
     foi gerada localmente (24 bytes aleatórios), hasheada e comunicada ao
     usuário uma única vez pelo chat, com instrução explícita para guardá-la
     — nunca ficou em nenhum arquivo do repositório nem em log persistente.
  6. Painel administrativo publicado no Cloudflare Pages (projeto criado com
     `wrangler pages project create`, branch de produção `production`); a URL
     não é documentada em materiais públicos.
  7. `assets/app.js` do painel passou a apontar, por padrão, para a URL real
     do Worker (antes usava `location.origin`, que aponta para a origem
     errada já que painel e Worker não compartilham domínio).
  8. `DASHBOARD_ORIGIN` do Worker atualizado para a URL real do painel;
     Worker republicado.
- **Dois bugs reais, encontrados só ao testar contra o deploy de verdade**
  (nenhum teste unitário pegou nenhum dos dois, o que está registrado
  honestamente no README do Worker):
  1. `PBKDF2_ITERATIONS = 210_000` (recomendação OWASP genérica) excedia o
     limite de 100.000 iterações que o runtime dos Workers (BoringSSL, não
     o OpenSSL do Node) aceita para PBKDF2 — todo login lançava
     `NotSupportedError`. Corrigido para 100.000 (ainda aceito pelo OWASP),
     com teste de regressão novo garantindo que o padrão nunca ultrapasse
     esse teto de novo. Hash de senha regenerado e secret atualizado.
  2. O cookie de sessão usava `SameSite=Strict`, que nunca é enviado em
     requisições cross-site — e painel (`*.pages.dev`) e Worker
     (`*.workers.dev`) são domínios registráveis diferentes de verdade em
     produção (diferente do teste local, onde eram só portas do mesmo
     `localhost`, daí o guia anterior não ter pego esse problema). O login
     em si funcionava (retornava sucesso), mas o cookie nunca voltava nas
     chamadas seguintes, prendendo o painel na tela de login. Corrigido
     para `SameSite=None` (exige `Secure`, que já estava presente).
- **Validação end-to-end real, no navegador de verdade** (não simulada):
  evento de teste enviado via `curl` para o Worker publicado, login feito
  na UI real do painel em `fivemcleaner-dashboard.pages.dev`, confirmado
  que os tiles e gráficos refletiram o evento (1 otimização, 100% de
  sucesso, 42s, CPU `AMD Ryzen 5 5600X`). A linha de teste foi apagada do
  banco real em seguida (`DELETE FROM telemetry_events`/
  `telemetry_event_actions`) para não deixar dado fictício misturado com
  dados reais futuros.
- **Decisão deliberada, não tomada nesta etapa**: o cliente .NET continua
  enviando telemetria pelo FormSubmit — `RemoteServicesOptions.
  TelemetryEndpoint` permanece `null` nos dois arquivos de configuração.
  Ligar o cliente ao Worker recém-publicado é uma mudança de comportamento
  do app distribuído a usuários finais e foi tratada como uma decisão
  separada, não implícita em "publicar a infraestrutura" — o usuário deve
  confirmar explicitamente antes dessa troca.
- **Simplificação de topologia**: as seções `env.development`/
  `env.production` do `wrangler.toml` (Workers nomeados separados) ficaram
  sem uso — um único Worker (ambiente padrão, implantado com `wrangler
  deploy` sem `--env`) atende telemetria de ambos os ambientes, já que a
  coluna `environment` de cada linha já faz essa distinção para o painel.
  As seções foram mantidas, mas não usadas, para uma eventual necessidade
  futura real de separação física.
- Validação: `dotnet build`/`dotnet test` — 486 testes .NET inalterados;
  Worker — 82 testes (`npm test`); painel — 28 testes (`npm test`). Total
  de 596 testes automatizados. `scripts\Verify-Safety.ps1` aprovado.
- Push de desenvolvimento autorizado explicitamente pelo usuário nesta
  etapa: todos os commits locais acumulados desde a última sincronização
  foram enviados para `origin/dev/proxima-versao` ao final desta tarefa —
  ver `git log` para a lista completa; nenhuma alteração em `main`, tag ou
  versão pública.

## Ajustes na janela de consentimento, histórico e reforço do padrão de bandeja (26/07/2026)

- Trabalho local, **não publicado** (nenhuma alteração em `main`, tag ou
  versão pública nesta etapa).
- **`PrivacyConsentWindow` restrita ao aplicativo**: a janela permitia
  arrastar (handler de `MouseLeftButtonDown` chamando `DragMove()`) e
  redimensionar (`ResizeMode="CanResize"` + `ResizeBorderThickness="7"`),
  divergindo do padrão já usado por `OptimizationConfirmationWindow`.
  Corrigido para `ResizeMode="NoResize"`, `ResizeBorderThickness="0"` e
  remoção completa do handler de arraste — a janela permanece centralizada
  sobre a `MainWindow` (`WindowStartupLocation="CenterOwner"`), sem título
  arrastável, e o único jeito de navegar o conteúdo é a rolagem já existente
  no `ScrollViewer`. Limitação conhecida do WPF: uma `Window` própria
  (mesmo com `Owner` definido) não é literalmente recortada dentro dos
  limites em pixels da janela principal — não arrastar/redimensionar é o
  que efetivamente restringe o comportamento pedido; um contêiner realmente
  clipado exigiria trocar a janela por um overlay embutido na própria
  `MainWindow`, mudança de arquitetura maior não feita aqui.
- **Removido o selo "Sem telemetria" da aba Histórico**: o texto
  (`History.NoTelemetry`) antecedia a funcionalidade de telemetria e ficou
  incorreto assim que o consentimento passou a existir — o app pode, sim,
  enviar telemetria quando autorizado. Removido o `TextBlock` do
  `MainWindow.xaml` e a chave não utilizada dos dois `.resx`.
- **"Minimizar para a bandeja" confirmado como padrão `true`** para
  instalação nova e para atualização de instalação antiga sem esse campo
  (`AppSettings.MinimizeToTrayOnClose = true`, sem nenhum passo do
  instalador que sobrescreva `settings.json`). Reforçado com dois novos
  testes dedicados (`Deserialize_JsonWithoutMinimizeToTrayOnClose_
  DefaultsToTrue` e `Deserialize_OldInstallationThatExplicitlyDisabled
  MinimizeToTray_PreservesTheChoice`), no mesmo padrão já usado para os
  toggles de telemetria/crash reports, para que uma regressão futura desse
  padrão específico nunca passe despercebida.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa foi de
  486 para **488 testes aprovados**; `scripts\Verify-Safety.ps1` aprovado.
- Push de desenvolvimento autorizado explicitamente pelo usuário nesta
  etapa.

## Correções de UI, instância única e confiabilidade da notificação de atualização (26/07/2026)

- Trabalho local, **não publicado**, e por instrução explícita do usuário
  desta vez **sem push de desenvolvimento** ao final — fica só commitado
  localmente.
- **1) Botão "Ver detalhes" do banner de atualização**: usava
  `Background="Transparent"`/`BorderThickness="0"` manuais mas mantinha o
  `ControlTemplate` padrão do `Button`, então o WPF continuava desenhando o
  retângulo azul de foco/hover ao redor dele — o mesmo bug já corrigido
  antes para o link "Reportar um bug". Corrigido aplicando
  `Style="{StaticResource LinkButtonStyle}"` (o mesmo estilo, com
  `ControlTemplate` só de `ContentPresenter`, sem visual de foco). Teste de
  regressão novo (`ReleaseNotesLinkButton_UsesLinkButtonStyleInsteadOf
  TheDefaultButtonChrome`) garante que esse botão específico nunca volte a
  usar o template padrão.
- **2) Investigação de acúmulo de processos/ícones na bandeja**: confirmado
  que o app **nunca teve controle de instância única** — qualquer novo
  lançamento (clique duplo repetido, atalho de Desenvolvimento e instalado
  rodando ao mesmo tempo, ou um processo anterior que não fechou direito)
  cria um processo novo, cada um com seu próprio `NotifyIcon`, explicando os
  "3 FiveMCleaner" vistos na bandeja. Adicionado `SingleInstanceGuard`
  (`Mutex` nomeado, com o nome incluindo o ambiente —
  `Local\FiveMCleaner.SingleInstance.Development`/`...Production`):
  - Escopo deliberadamente **por ambiente, não global** — rodar a build de
    Desenvolvimento e uma cópia instalada de Produção ao mesmo tempo
    continua funcionando (fluxo já documentado em
    `scripts/Start-DevelopmentApp.ps1`); só duplicar a *mesma* build é
    bloqueado.
  - `App.xaml.cs.OnStartup` tenta adquirir o mutex antes de criar
    qualquer janela; se já estiver em uso, mostra uma mensagem localizada
    ("O FiveMCleaner já está em execução...") e encerra o processo
    novo sem nunca criar `MainWindow` nem o ícone de bandeja.
  - Modo demo (`--demo`/`--demo-synthetic`, usado por smoke tests
    automatizados) fica deliberadamente isento — ferramentas de automação
    podem legitimamente lançar essa build repetidamente.
  - 7 testes novos cobrindo o nome do mutex por ambiente, aquisição pelo
    primeiro chamador, bloqueio real de um segundo chamador (usando uma
    thread nativa separada para reproduzir a semântica real de
    reentrância por thread do Windows, já que duas aquisições na mesma
    thread de teste "sucederiam" trivialmente sem provar nada), liberação
    após `Dispose` e `Dispose` seguro mesmo sem nunca ter adquirido.
- **3) Confiabilidade da notificação nativa de atualização**: revisão do
  código encontrou um problema real e bem documentado do
  `System.Windows.Forms.NotifyIcon`: chamar `ShowBalloonTip` no mesmo
  instante em que `Visible` é definido como `true` pela primeira vez pode
  ser silenciosamente ignorado pelo Windows (o host da bandeja precisa de
  um instante para registrar o ícone antes de aceitar um balão nele) — o
  que acontecia exatamente no caso descrito pelo usuário (app sem
  "minimizar para a bandeja" ativo, ícone criado só para a notificação).
  Corrigido: `TrayIconService.ShowUpdateAvailable` agora aguarda ~300ms
  entre tornar o ícone visível e efetivamente chamar `ShowBalloonTip`
  quando o ícone não estava visível antes; quando já estava visível
  (minimizado para a bandeja), a notificação continua imediata. O texto
  da notificação (título + versão + "clique para abrir") já estava bom nas
  duas línguas, sem necessidade de alteração de texto.
  **Lacuna conhecida e assumida conscientemente**: `TrayIconService` não
  tem cobertura de teste automatizado (nenhuma classe deste projeto tinha
  antes) — `NotifyIcon` exige um shell/bandeja real do Windows para
  funcionar de verdade, fora do alcance de `dotnet test`. A correção foi
  validada por revisão de código contra um comportamento amplamente
  documentado do Win32/WinForms, não por execução automatizada; recomenda-
  se validação manual numa máquina Windows real antes de divulgar.
- Validação: `dotnet build` Release sem avisos/erros; suíte completa foi de
  488 para **496 testes aprovados**, estável em execuções repetidas;
  `scripts\Verify-Safety.ps1` aprovado.
- Por instrução explícita do usuário, **sem push de desenvolvimento** nesta
  etapa — os commits ficam só locais até uma próxima sincronização.

## Remoção definitiva do FormSubmit, relato de bug no Worker/R2, aba "Bugs reportados" e checagem manual de atualização (26/07/2026)

- Trabalho local, **não publicado**; por instrução explícita do usuário,
  **um único commit local** ao final, **sem push, sem deploy, sem PR**.
- **1) Telemetria — FormSubmit removido de vez.**
  `FormSubmitAnonymousTelemetryService` foi deletado por completo de
  `AnonymousTelemetryService.cs` (a lógica de `ClassifyException` que ele
  continha virou o novo `TelemetryErrorClassifier`, estático e independente
  de transporte). `MainWindow.xaml.cs` não tem mais um fallback para
  FormSubmit: se `RemoteServicesOptions.TelemetryEndpoint` não for uma URL
  HTTPS válida, cai em `DisabledAnonymousTelemetryService` (telemetria
  desligada, nunca um envio silencioso a um serviço diferente). Os dois
  `appsettings.{Development,Production}.json` apontam
  `telemetryEndpoint` para a rota `/telemetry` do Worker já publicado
  (`https://fivemcleaner-telemetry.felipemarquesini10.workers.dev/telemetry`)
  — essa parte **já funciona de verdade**, sem depender de nenhum deploy
  novo, pois o Worker com essa rota já estava no ar de uma sessão anterior.
- **2) Relato de bug movido para o Worker, com anexo em R2.** Perguntado
  explicitamente como tratar o anexo de captura de tela (D1 não é adequado
  para blobs), o usuário escolheu **provisionar R2 agora também** em vez de
  remover o anexo. Implementado:
  - `FormSubmitBugReportService` removido; `CloudflareBugReportService.cs`
    (novo) reaplica toda a validação client-side já existente (categoria,
    resumo, descrição, versão, perfil, resumo técnico, assinatura PNG do
    anexo) e envia JSON (anexo em `contentBase64`) para a rota `/bugs` do
    Worker. `DisabledBugReportService` é o fallback quando
    `BugReportEndpoint` não está configurado — mensagem clara de "não
    configurado", nunca um envio silencioso a outro lugar.
  - `RemoteServicesOptions.BugReportEndpoint` (novo campo) aponta para
    `.../bugs` nos dois `appsettings.*.json`.
  - Worker: `infra/cloudflare-worker/src/bugReports/validateSubmission.js`
    (validação pura, espelhando as mesmas regras do lado .NET, incluindo
    checagem de assinatura de bytes PNG — nunca confiar só na validação do
    cliente) e `queries.js` (listagem paginada/filtrada por ambiente e
    categoria para o painel). Tabela `bug_reports` nova em `schema.sql`.
    Três rotas novas em `index.js`: `POST /bugs` (ingestão pública, grava
    em D1 e no bucket R2 `BUG_REPORT_ATTACHMENTS`), `GET /api/bugs`
    (listagem autenticada) e `GET /api/bugs/:id/attachment` (streaming
    autenticado do anexo a partir do R2). `wrangler.toml` ganhou o binding
    `[[r2_buckets]]` para `fivemcleaner-bug-reports`.
  - **Decisão consciente e assumida**: a rota `/bugs` e o bucket R2 são
    **código-completo e testado, mas não implantados** — isso exigiria
    `wrangler deploy` e `wrangler r2 bucket create
    fivemcleaner-bug-reports`, ações remotas que a instrução desta sessão
    ("apenas um commit local") não autorizou. Até esse redeploy explícito
    acontecer em uma sessão futura, o botão "Enviar relato" do app instalado
    falha com uma mensagem clara em vez de continuar indo para o FormSubmit
    removido.
- **3) Painel administrativo — aba "Bugs reportados".** `infra/dashboard`:
  `index.html` ganhou uma quarta seção com uma tabela (Quando, Categoria,
  Resumo, Versão, Perfil, Ambiente, Captura); `assets/api.js` ganhou
  `buildBugsUrl`/`buildBugAttachmentUrl`; `assets/charts.js` ganhou
  `truncate`/`toBugReportRow` (resumo truncado em 60 caracteres, "sim"/"não"
  para presença de anexo); `assets/app.js` busca `/api/bugs` em paralelo com
  as outras estatísticas e renderiza a tabela, com link "ver captura"
  (`target="_blank"`) quando há `attachment_key`. Sem esses dados reais até
  o redeploy do item 2 acontecer, a tabela mostra "Sem dados ainda" como as
  outras.
- **4) "Procurar atualizações" manual em Configurações.** A checagem
  automática existente (`MainViewModel.CheckForUpdatesAsync`) é silenciosa
  e se auto-bloqueia (`return` antecipado) se já houver uma atualização
  conhecida — não serve para um botão que o usuário clica e espera uma
  resposta explícita sempre. Adicionado
  `CheckForUpdatesManuallyAsync()`, um método irmão que:
  - sempre dispara uma checagem nova contra `IReleaseUpdateService`
    (mesmo que uma atualização já tenha sido detectada antes);
  - se encontrar uma atualização, ativa o banner já existente
    (`IsUpdateBannerVisible`) do jeito normal;
  - se não encontrar, define `ManualUpdateCheckMessage` com o texto
    localizado "Você já está na última versão publicada." — nunca fica em
    silêncio;
  - em falha de rede/transporte, define `ManualUpdateCheckMessage` com o
    erro em vez de deixar a UI parada sem explicação;
  - expõe `IsCheckingForUpdatesManually`/`CanCheckForUpdatesManually` para
    desabilitar o botão durante a checagem em andamento.
  Botão novo em `MainWindow.xaml` (aba Configurações, mesmo padrão visual
  do botão de benchmark do GTA V: `SecondaryButtonStyle` + rótulo de status
  abaixo), strings novas em `Strings.resx`/`Strings.pt-BR.resx`
  (`Settings.CheckForUpdates.*`, `Update.ManualCheck.*`).
- **Testes novos**: `TelemetryErrorClassifierTests`,
  `DisabledAnonymousTelemetryServiceTests`, `CloudflareBugReportServiceTests`,
  `DisabledBugReportServiceTests` (lado .NET); `MainViewModelUpdateCheckTests`
  (5 testes cobrindo os cenários acima da checagem manual, com um
  `FakeReleaseUpdateService` novo); `validateSubmission.test.js` (14 testes)
  e `queries.test.js` (8 testes) no Worker; testes de `api.js`/`charts.js`
  ampliados no painel para as novas funções de bug report.
- Validação: `dotnet build` Release sem avisos/erros; suíte .NET completa
  passou de 496 para **503 testes aprovados**; `scripts\Verify-Safety.ps1`
  aprovado; `npm test` em `infra/cloudflare-worker` (104 testes) e
  `infra/dashboard` (36 testes), ambos aprovados.
- Por instrução explícita do usuário, **um único commit local** ao final
  desta etapa, sem push, sem deploy e sem PR — a rota `/bugs` e o bucket R2
  continuam pendentes de uma ativação remota futura e explicitamente
  autorizada.

## Push de desenvolvimento, deploy real do Worker e remoção do anexo/R2 do relato de bug (26/07/2026)

- Nesta etapa o usuário autorizou explicitamente operações remotas: **push
  de desenvolvimento** de todos os commits locais pendentes, **`wrangler
  deploy`** do Worker e **`wrangler r2 bucket create`** para o anexo de
  captura de tela do relato de bug.
- **Push de desenvolvimento**: os 2 commits locais pendentes (a sessão
  anterior de remoção do FormSubmit + esta) foram enviados para
  `origin/dev/proxima-versao` — nunca `main`, sem PR, sem tag, como sempre.
- **Bloqueio real do R2**: `wrangler r2 bucket create
  fivemcleaner-bug-reports` falhou com `Please enable R2 through the
  Cloudflare Dashboard [code: 10042]` — R2 exige uma ativação de produto na
  conta (aceitar termos, possivelmente confirmar billing mesmo no free
  tier) que só é feita pelo painel web da Cloudflare, não pela CLI/API.
  Perguntado como prosseguir, o usuário decidiu **remover anexo/captura de
  tela do relato de bug por completo**, em vez de esperar a ativação do R2.
- **Relato de bug agora é só texto, sem R2**: removido de vez de todo o
  projeto — `BugReportAttachment`/`BugReportImageProcessor.cs` (e seu teste)
  deletados; `BugReportWindow.xaml`/`.xaml.cs` perderam a seção de anexo
  (seletor de arquivo, sanitização de imagem, botões "Selecionar"/"Remover")
  e ganharam dois campos novos, ambos opcionais: **e-mail** (validado por
  regex simples, nunca obrigatório) e **trecho de log em texto puro**
  (limitado a 100 KB, verificado tanto no app quanto de novo no Worker).
  `BugReportSubmission.Attachment` virou `Email`/`LogText`.
  `CloudflareBugReportService` não envia mais `attachment` no payload, envia
  `email`/`logText`; a validação de assinatura PNG e o limite de 8 MB
  desapareceram.
- **Worker**: `validateSubmission.js` perdeu toda a validação de anexo
  (assinatura PNG, nome de arquivo, base64) e ganhou validação de e-mail
  (regex) e log (limite de bytes UTF-8). `index.js` perdeu
  `handleBugReportAttachment` e a rota `GET /api/bugs/:id/attachment`, e o
  `INSERT` em `handleBugReportIngest` não grava mais em R2 nem em
  `attachment_key` — grava `email`/`log_text`. `queries.js` seleciona
  `email`/`log_text` em vez de `attachment_key`. `wrangler.toml` não declara
  mais o binding `[[r2_buckets]]`.
- **Painel administrativo**: coluna "Captura" (com link "ver captura") saiu
  da tabela "Bugs reportados"; entraram colunas "E-mail" e "Log" (esta
  última só indica "sim"/"não", já que o texto completo do log não precisa
  aparecer na tabela). `buildBugAttachmentUrl` removida de `api.js`.
- **Migração de dados real, no D1 de produção** (`fivemcleaner-telemetry`,
  banco já existente de sessões anteriores): a tabela `bug_reports` foi
  criada remotamente (`CREATE TABLE IF NOT EXISTS`, ela ainda não existia de
  fato no banco, apesar de estar no `schema.sql` desde a sessão anterior),
  depois alterada com `ALTER TABLE ... ADD COLUMN email/log_text` e
  `ALTER TABLE ... DROP COLUMN attachment_key` — tudo via `wrangler d1
  execute --remote`, comandos individuais, sem perda de dados (a tabela
  estava vazia antes desta sessão).
- **Deploy real do Worker**: `wrangler deploy` (sem `--env`, o único
  ambiente de fato usado) publicou a versão atual do Worker — agora **as
  rotas `/telemetry`, `POST /bugs` e `GET /api/bugs` estão todas ao vivo em
  produção** em
  `https://fivemcleaner-telemetry.felipemarquesini10.workers.dev`, sem
  qualquer binding de R2. Validado com um envio sintético real via `curl`
  contra `/bugs` (HTTP 202, `{"success":true}`), confirmado no D1 via
  `wrangler d1 execute --remote` e removido logo em seguida (dado de teste,
  não um relato real).
- **O que isso significa para quem já usa o app hoje**: nada muda
  automaticamente. Telemetria e relato de bug via Worker só passam a valer
  para quem instalar a **próxima versão pública** do app — quem já tem uma
  versão instalada continua rodando o código antigo (FormSubmit) até
  atualizar. A infraestrutura (Worker, D1, rotas) já está pronta e ao vivo
  agora; falta só a próxima versão pública ser lançada para o app
  publicado de fato usar essa infraestrutura.
- Validação: `dotnet build`/`dotnet test` Release (503 testes, sem
  regressão); `npm test` em `infra/cloudflare-worker` (104 testes,
  reescritos para o esquema sem anexo) e `infra/dashboard` (35 testes);
  `scripts\Verify-Safety.ps1` aprovado.

## Correção do "falha em 4 passos, funciona como administrador" no perfil Médio/Agressivo (26/07/2026)

- Trabalho local, **um único commit local**, **sem push de desenvolvimento**
  por instrução explícita do usuário desta vez.
- **Contexto**: o usuário relatou que o bug já investigado em 24/07/2026
  ("perfil Médio falha ao abrir o app normalmente, funciona como
  administrador") continua ocorrendo, agora observado no perfil Médio e
  suspeito nos demais. O usuário fez sua própria investigação de causa raiz
  lendo o código-fonte e propôs uma solução detalhada, que foi verificada
  linha a linha antes de implementar (nenhuma mudança foi feita sem
  confirmar contra o código real primeiro — `superpowers:systematic-debugging`).
- **Causa raiz nº 1 (a mais importante — o "efeito cascata")**: no plano
  Médio/Agressivo, há **uma única** ação administrativa no catálogo inteiro,
  `EnableSessionPerformancePowerPlan` (ativa o plano de energia de alto
  desempenho). Quando o broker elevado falhava por qualquer motivo (UAC
  cancelado, SmartScreen/antivírus interrompendo o processo elevado sem
  assinatura, etc.), `AppOptimizationService.ExecutePlanCoreAsync` chamava
  `runtime.Engine.RollbackAsync(..., IncludeStandardUserActions: true)` —
  desfazendo **todas** as ações de usuário padrão já confirmadas com
  sucesso (Modo de Jogo, preferência de GPU, captura em segundo plano,
  etc.). O relatório então mostrava várias ações como revertidas/falhas
  quando, na real, só a ativação do plano de energia tinha dado errado —
  exatamente como o usuário descreveu ("o eletricista desmontando a casa
  inteira porque uma lâmpada queimou").
- **Correção**: novo método
  `WindowsTransactionEngine.MarkAdministratorPhaseFailedAsync(transactionId, reason)`
  marca **somente** as ações administrativas ainda `Pending`/`DeferredPrivilege`
  como `Failed` no journal, sem tocar nas ações já `Committed`; a transação
  se estabiliza em `CommittedWithErrors` em vez de `RolledBack`.
  `AppOptimizationService` foi atualizado para chamar esse método em vez do
  rollback completo, com duas novas mensagens localizadas
  (`Runtime.UacCancelledPreserved`/`Runtime.AdminPhaseFailedPreserved`)
  deixando explícito que "a otimização foi concluída, mas o Windows não
  permitiu [ação específica]; as demais alterações foram mantidas".
- **Causa raiz nº 2 (por que o UAC aparecia sempre)**: `SessionPerformancePowerPlanAction.ApplyAsync`
  tinha um `throw new UnauthorizedAccessException(...)` incondicional logo
  no início se `!context.IsElevated` — mesmo em máquinas onde o Windows
  permite a um usuário comum trocar o plano de energia ativo (comum fora de
  ambientes corporativos com Política de Grupo restritiva). Isso forçava
  elevação sempre, mesmo quando desnecessária.
- **Correção**: novo campo `ActionMetadataDto.AttemptWithoutElevationFirst`
  (só `true` para `EnableSessionPerformancePowerPlan` até agora). O motor
  (`WindowsTransactionEngine`, tanto no modo `IsolateFailures` quanto no
  modo padrão) agora inclui uma ação administrativa marcada com esse campo
  na fase de usuário padrão mesmo sem elevação; se a ação lançar
  `UnauthorizedAccessException`, o motor a devolve para
  `DeferredPrivilege` (como se nunca tivesse sido tentada) em vez de
  marcá-la como falha — só então a fase elevada (broker/UAC) é acionada.
  `PowerCfgController.TryActivatePerformanceSchemeAsync` deixou de retornar
  só `bool`; agora retorna `PowerPlanActivationOutcome`
  (`Activated`/`AccessDenied`/`SchemeUnavailable`), distinguindo "o Windows
  recusou por permissão" (código de saída 5/ERROR_ACCESS_DENIED ou texto
  "access is denied"/"acesso negado" no stderr do `powercfg`) de "este PC
  não expõe esse plano" — só o primeiro caso dispara elevação;
  `SessionPerformancePowerPlanAction.ApplyAsync` não tem mais o throw
  incondicional, só lança `UnauthorizedAccessException` quando o próprio
  Windows confirma acesso negado.
- **Diagnóstico do broker (item 4 da proposta do usuário)**: novo
  `BrokerDiagnosticsLog` (`src/FiveMCleaner.Broker/BrokerDiagnosticsLog.cs`)
  grava um log local, append-only, com `FileOptions.WriteThrough` (flush
  imediato por linha, sobrevive a um kill externo do processo) em
  `%LOCALAPPDATA%\FiveMCleaner\Logs\broker-diagnostics.log`. Cada linha tem
  só nome do evento + timestamp + ID de transação (nunca caminhos, nunca
  conteúdo do plano) — marcos: `broker-started`, `pipe-connected`/
  `pipe-connect-failed`, `elevation-confirmed`/`elevation-failed`,
  `request-loaded`, `plan-validated`, `action-started`, `journal-saved`,
  `execution-failed`/`execution-timeout`, `terminal-event-sent`,
  `rollback-requested`/`rollback-completed`/`rollback-failed`,
  `unhandled-exception`. Isso deve permitir, numa próxima ocorrência,
  distinguir exatamente em qual etapa o broker parou (cancelamento do
  usuário vs. bloqueio antes de iniciar vs. falha de pipe vs. antivírus
  encerrando o processo vs. `powercfg` recusando vs. falha real do
  Windows) — sem precisar reproduzir o bug de novo às cegas.
  **Lacuna conhecida e assumida conscientemente**: `BrokerDiagnosticsLog`
  não tem teste automatizado — o projeto `FiveMCleaner.Broker` não tem
  nenhuma cobertura de teste hoje (roda elevado, sem infraestrutura de
  teste própria), e adicionar isso agora seria desproporcional ao escopo
  desta correção; mesma categoria de lacuna já assumida antes para
  `TrayIconService`.
- **Não implementado desta vez** (deliberadamente fora de escopo, e
  registrado aqui para uma sessão futura): a matriz de testes de integração
  real do usuário (Windows 10/11, conta admin não elevada, conta padrão,
  credenciais de outro administrador digitadas no prompt do UAC — o cenário
  de `PipeOptions.CurrentUserOnly` quebrar quando o token elevado pertence a
  uma identidade Windows diferente da que abriu o pipe). Isso exige
  hardware/VMs reais para Windows, fora do alcance deste ambiente de
  desenvolvimento.
- **Testes novos**: `MarkAdministratorPhaseFailedAsync_PreservesAlreadyCommittedStandardActions`,
  `IsolatedExecution_AdministratorActionThatDoesNotNeedElevation_CommitsWithoutAwaitingUac`,
  `IsolatedExecution_AdministratorActionThatNeedsElevation_DefersInsteadOfFailingTheRun`
  em `WindowsTransactionEngineTests.cs`; três testes novos em
  `WindowsActionHandlerTests.cs` para os três resultados de
  `SessionPerformancePowerPlanAction.ApplyAsync` (ativa sem elevação,
  acesso negado sem elevação lança `UnauthorizedAccessException`, plano
  indisponível retorna `NoChange`).
- Validação: `dotnet build` Release sem avisos/erros; suíte completa
  passou de 503 para **509 testes aprovados**; `scripts\Verify-Safety.ps1`
  aprovado.
- Por instrução explícita do usuário, **sem push de desenvolvimento** nesta
  etapa — o commit fica só local.

## Classificação de um lote de otimizações gráficas propostas pelo usuário (26/07/2026)

- O usuário trouxe uma lista de otimizações gráficas do Windows que quer
  adicionar (GPU de alto desempenho, otimizações para jogos em janela do
  Windows 11, Fullscreen Optimizations, HAGS, Modo de Jogo, VRR, frequência
  do monitor, HDR/Auto HDR) e pediu para classificar cada item em
  Leve/Médio/Agressivo usando uma legenda (✅ automático seguro, 🟡
  opcional/condicional, 🧪 experimental com reversão automática, 🔧 reparo,
  👁 diagnóstico sem alterar, 🚫 não implementar).
- **Isto foi só classificação e planejamento, nenhum código novo foi
  escrito nesta rodada.** A decisão completa, item a item, está em
  [`docs/graphics-optimizations-backlog.md`](docs/graphics-optimizations-backlog.md)
  (novo arquivo).
- Resumo da decisão:
  - GPU de alto desempenho e Modo de Jogo: a maior parte **já está
    implementada** hoje (`windows.gaming.high-performance-gpu.prefer`,
    `windows.gaming.game-mode.enable`, ambas `AllProfiles`); só "detectar
    GPU integrada usada por engano" é novo, e é diagnóstico de baixo risco.
  - Janela sem bordas (Win11), Fullscreen Optimizations por app, HAGS e
    "desligar Modo de Jogo condicionalmente": todos classificados **🧪
    Experimental, só no perfil Agressivo, opt-in, com comparação
    antes/depois e reversão automática obrigatória** — nunca em Leve/Médio,
    nunca silencioso.
  - VRR e frequência do monitor: a parte de **detecção/diagnóstico** (👁)
    já está parcialmente coberta por `windows.gaming.display-configuration.diagnose`/
    `windows.gaming.session-settings.diagnose`, que já documentam a
    limitação real de "G-SYNC/FreeSync/VRR não têm API pública sem driver
    do fabricante". **Habilitar VRR programaticamente ficou pendente de
    pesquisa** (precisa entrar em `docs/research.md` como Fato/Inferência
    antes de qualquer código) — não foi classificado como implementável
    ainda. Trocar a frequência do monitor entra como 🟡 condicional, com
    confirmação obrigatória (Médio e Agressivo).
  - HDR/Auto HDR: **fora de qualquer perfil automático** — ativar é
    preferência visual manual (🟡), desativar por app vira reparo sob
    demanda (🔧) quando já existe um problema relatado; nunca apresentado
    como ganho de FPS (regra de copy/UI, não uma ação).
- Próxima sessão que for implementar qualquer um desses itens deve seguir
  a ordem sugerida no próprio backlog: primeiro os itens 🧪 do perfil
  Agressivo (reaproveitando a infraestrutura de comparação antes/depois já
  existente, `OptimizationComparisonResult`), depois os diagnósticos 👁
  (extensões de baixo risco dos diagnósticos já existentes), e só depois
  de pesquisa documentada o item de VRR habilitável.

## Implementação de 3 itens do backlog de otimizações gráficas (26/07/2026)

- O usuário autorizou explicitamente implementar (não só classificar) o
  lote de otimizações gráficas do item anterior. Catálogo de ações subiu de
  `CurrentVersion = 10` para `11` com 3 ações novas.
- **`windows.gaming.gpu-preference-mismatch.diagnose`** (👁, todos os
  perfis, `ActionOptionGate.Always`): `GpuPreferenceMismatchDiagnosisAction`
  em `DiagnosticActions.cs`. Cruza a detecção de GPU integrada+dedicada
  (`IGpuVendorInspector`, heurística por nome de driver) com a preferência
  de GPU já configurada para o FiveM no mesmo local de registro que
  `GpuPreferenceRegistryAction` já escreve
  (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`). Só leitura, nunca
  altera nada; deliberadamente não tenta confirmar qual GPU o jogo está de
  fato usando numa sessão ao vivo (isso exigiria hook de DXGI/ETW, fora de
  escopo).
- **`windows.gaming.fullscreen-optimizations.toggle`** (🧪, **Agressivo
  apenas**, opt-in via `OptimizationOptionsDto.ToggleFullscreenOptimizationsExperiment`):
  `FullscreenOptimizationsRegistryAction` em `RegistryActions.cs`, baseada
  no mesmo padrão de `AllowlistedRegistryAction` (merge/preserva outras
  flags, snapshot/rollback byte-a-byte) já usado por
  `GpuPreferenceRegistryAction`. Alterna a flag de compatibilidade
  `DISABLEDXMAXIMIZEDWINDOWEDMODE` em
  `HKCU\...\AppCompatFlags\Layers` para FiveM.exe e (quando detectado)
  GTA5.exe. Documentado explicitamente como mecanismo de **convenção
  observada, não API oficial da Microsoft** — por isso a reversibilidade
  exata (não a correção semântica da flag) é o que garante a segurança.
- **`windows.gaming.hags.toggle`** (🧪, **Agressivo apenas**, opt-in via
  `OptimizationOptionsDto.ToggleHagsExperiment`,
  `RequiredPrivilege.Administrator`, `RequiresRestart = true`): `HagsToggleAction`
  em `RegistryActions.cs`, alterna `HwSchMode` (`HKLM\SYSTEM\...\GraphicsDrivers`)
  para o estado oposto ao atual (nunca uma direção fixa — "testar ligado e
  desligado" literal). Reaproveita o mecanismo `AttemptWithoutElevationFirst`
  criado na sessão anterior (mesmo padrão do plano de energia): tenta sem
  admin primeiro (praticamente sempre falha por ACL do HKLM, mas é
  consistente com o resto do código), só depois aciona o broker.
- **O que ficou de fora, deliberadamente**: a comparação automática de
  frametime/latência antes-e-depois com decisão automática de reverter
  (a parte "🧪 completa" do item) não foi implementada — só o mecanismo
  seguro de aplicar/reverter. Isso seguiria o mesmo padrão já usado por
  outras opções "opt-in, nunca automáticas" deste projeto (ex.:
  `ApplyGtaVRepairLaunchParameters`): o usuário ativa, testa manualmente, e
  reverte pelo histórico. Automatizar a medição exigiria orquestrar um
  benchmark real em torno de cada toggle — trabalho maior e separado,
  registrado em `docs/graphics-optimizations-backlog.md`.
- **Sem UI nova**: os dois toggles opt-in não têm checkbox no
  `MainWindow.xaml` ainda — consistente com o padrão já estabelecido neste
  projeto (`TerminateStuckFiveMProcess`, `RecreateFiveMLocalData`,
  `ApplyGtaVRepairLaunchParameters` etc. também não têm UI própria hoje).
- **Continuam fora de escopo** (não implementados, motivo técnico/segurança
  documentado em `docs/graphics-optimizations-backlog.md`): otimizações
  para jogos em janela do Windows 11, habilitar VRR programaticamente,
  troca automática de frequência do monitor, e qualquer toggle de HDR/Auto
  HDR — todos exigiriam validação em hardware real que este ambiente não
  tem, ou não têm mecanismo público confirmado.
- **Testes novos**: `MarkAdministratorPhaseFailedAsync`... (já cobertos
  antes); desta sessão:
  `GpuPreferenceMismatch_FlagsDualGpuLaptopWithoutHighPerformancePreferenceConfigured`,
  `GpuPreferenceMismatch_ReportsAlreadyConfiguredWhenPreferenceIsSet`,
  `GpuPreferenceMismatch_SkipsSingleGpuMachines` (`DiagnosticActionsTests.cs`);
  `HagsToggle_FlipsToTheOppositeStateAndRollbackRestoresOriginal`,
  `HagsToggle_FlipsBackToDisabledWhenCurrentlyEnabled`,
  `HagsToggle_TreatsMissingValueAsDefaultAndFlipsToEnabled`,
  `FullscreenOptimizations_TogglesFlagForFiveMAndGtaVPreservingOtherFlags`,
  `FullscreenOptimizations_TogglesBackOffWhenAlreadyDisabled`
  (`WindowsActionHandlerTests.cs`); `GraphicsExperiments_AreOptInAggressiveOnlyAndNeverPartOfAnyDefaultProfile`
  (`PlanBuilderTests.cs`); `ElevatedActions_AreExplicitlyMarkedAndReversible`
  atualizado para as duas ações administrativas agora existentes
  (`ActionCatalogTests.cs`).
- **Bug pego durante o próprio desenvolvimento**: a primeira versão de
  `FullscreenOptimizationsRegistryAction.ToggleFlag` re-adicionava a flag
  quando ela era o único token e estava sendo removida (retornava a flag de
  volta em vez de string vazia quando a lista de tokens ficava vazia) —
  corrigido antes do commit, coberto pelo teste
  `FullscreenOptimizations_TogglesBackOffWhenAlreadyDisabled`.
- Validação: `dotnet build`/`dotnet test` Release (518 testes, eram 509);
  `scripts\Verify-Safety.ps1` aprovado.

## Classificação do lote "Driver e perfil NVIDIA" + G-SYNC (26/07/2026, terceira rodada)

- Terceira leva de otimizações propostas pelo usuário (driver/perfil NVIDIA
  e G-SYNC), classificada com a mesma legenda das rodadas anteriores. Desta
  vez **só classificação e documentação, sem autorização explícita de
  implementação** (diferente da rodada anterior) — nenhum código novo foi
  escrito. Decisão completa em
  [`docs/graphics-optimizations-backlog.md`](docs/graphics-optimizations-backlog.md)
  (seções 9 e 10, novas).
- **Decisão central desta rodada**: quase toda a lista de "configurações
  possíveis" do driver NVIDIA (baixa latência, limite de FPS pelo driver,
  G-SYNC por aplicativo, Shader Cache Size, Texture Filtering Quality,
  Threaded Optimization, NVIDIA Image Scaling, DSR, gerenciamento de
  energia por app, criar perfil por aplicativo) foi classificada **🚫 Não
  implementar** pelo mesmo motivo técnico: a NVIDIA não publica uma API
  oficialmente suportada para escrever no perfil 3D por aplicativo — isso
  já era a política registrada em `docs/safety.md`
  ("ajustes de perfil 3D devem ser feitos apenas pelo painel oficial do
  fabricante... o FiveMCleaner não escreve nem sobrescreve esses perfis"),
  então esta rodada só confirma e documenta essa regra contra a lista
  específica pedida, não inventa uma decisão nova.
- O que sobrou como implementável (ainda não implementado, registrado como
  backlog): diagnóstico de driver muito antigo (extensão do diagnóstico de
  versão já existente), detecção de gravação instantânea (NVIDIA Instant
  Replay) e filtros Freestyle ativos (extensão do detector de streaming já
  existente, só leitura), orientação (nunca ativação automática) de G-SYNC
  e seu indicador on-screen, e reinstalação limpa de driver guiada (🔧,
  passo a passo manual, nunca executada pelo app). O limite de FPS
  compatível com a faixa do monitor **já está coberto** pelo `-frameLimit`
  existente em `gtav.legacy.launch-parameters.graphics.apply`, que não
  depende do driver NVIDIA.
- Nenhuma mudança de código, teste ou build nesta etapa — só documentação.

## Implementação do lote NVIDIA driver/G-SYNC que dava para fazer (26/07/2026, quarta rodada)

- O usuário pediu para implementar "tudo que der" do lote classificado na
  rodada anterior. Catálogo subiu de `CurrentVersion = 11` para `12` com 2
  ações novas e 2 diagnósticos existentes ampliados.
- **`windows.gaming.gsync.guide`** (👁, todos os perfis,
  `ActionOptionGate.Always`): `GSyncGuidanceDiagnosisAction` em
  `HardwareDiagnosticActions.cs`. Reaproveita
  `IDisplayConfigurationInspector.MaxRefreshHzAtCurrentResolution` (já
  existente) para sugerir um limite de FPS alguns quadros abaixo do máximo
  detectado (recomendação real da NVIDIA para manter o FPS dentro da faixa
  variável do G-SYNC), referenciando o `-frameLimit` que o app já aplica.
  Nunca ativa G-SYNC sozinho — não existe API pública para isso.
- **`windows.system.driver-reinstall.guide`** (🔧, opt-in via
  `OptimizationOptionsDto.GuideDriverReinstall`, todos os perfis quando
  ativado): `GuidedDriverReinstallAction` — texto informativo com os passos
  oficiais (DDU em Modo de Segurança + instalador mais recente do site do
  fabricante). Nunca baixa, instala ou remove nenhum driver; risco real de
  tela sem vídeo se o passo a passo for feito errado é do usuário, não do
  app, e o texto deixa isso explícito.
- **`DiagnoseDriverVersions` ampliado**: `DriverVersionInfo` ganhou
  `DriverDate` (novo campo opcional, lido de
  `Win32_PnPSignedDriver.DriverDate` via parsing do formato DMTF do WMI).
  `DriverVersionsDiagnosisAction.ClassifyOldDrivers(snapshot, now)` alerta
  quando o driver de vídeo está há mais de 18 meses sem atualização — sinal
  objetivo (data real do driver), nunca um palpite pela string de versão
  (que cada fabricante formata de um jeito).
- **`DetectOverlaysAndCaptureSoftware` ampliado**: o detector de overlays
  já reconhecia o processo real do Instant Replay/ShadowPlay ("NVIDIA
  Share") desde antes; a mensagem passou a mencionar isso explicitamente
  quando detectado, e também menciona (sem afirmar como fato, já que não
  há sinal de processo isolado) que filtros do Freestyle podem estar em
  uso — cobre "detectar gravação instantânea ativa" e parcialmente
  "detectar filtros Freestyle ativos" do lote proposto.
- **O que ficou de fora, com justificativa técnica**: praticamente toda a
  lista de configurações do perfil 3D por aplicativo da NVIDIA (baixa
  latência, limite de FPS pelo driver, G-SYNC por aplicativo, Shader Cache
  Size, Texture Filtering Quality, Threaded Optimization, NVIDIA Image
  Scaling, DSR, gerenciamento de energia por app, criar perfil por
  aplicativo, desativar overlay automaticamente) — a NVIDIA não publica uma
  API oficialmente suportada para escrever essas configurações; essa não é
  uma decisão nova desta sessão, é a mesma política já registrada em
  `docs/safety.md` aplicada de forma consistente contra a lista pedida.
- **Testes novos**: `ClassifyOldDrivers_FlagsAVideoDriverOlderThan18Months`,
  `ClassifyOldDrivers_ReturnsNullWhenDriverIsRecentOrDateIsUnknown`,
  `GSyncGuidance_SuggestsFpsCapBelowMaxRefreshWhenKnown`,
  `GSyncGuidance_StillOrientsWhenRefreshRateIsUnavailable`,
  `GuidedDriverReinstall_NeverTouchesAnythingAndExplainsTheOfficialSteps`
  (`HardwareDiagnosticActionsTests.cs`);
  `OverlaySoftwareDetectionAction_MentionsInstantReplayAndFreestyleWhenShadowPlayIsDetected`
  (`DiagnosticActionsTests.cs`);
  `GuideDriverReinstall_IsOptInAndNeverPartOfAnyDefaultProfile`
  (`PlanBuilderTests.cs`); listas de ação esperadas em
  `LightProfile_UsesOnlyLowImpactStandardUserActions`/
  `DisabledOptions_RemoveTheirActionsButKeepSafetyPreflight` atualizadas
  para incluir o novo diagnóstico G-SYNC (sempre ativo).
- Validação: `dotnet build`/`dotnet test` Release (525 testes, eram 518);
  `scripts\Verify-Safety.ps1` aprovado.
- Por instrução explícita do usuário, **apenas commit local** nesta etapa
  — sem push de desenvolvimento.

## Implementação do lote "Driver e perfil AMD" (26/07/2026, quinta rodada)

- O usuário mandou o lote AMD já pedindo implementação direta ("Implementar:")
  e push de desenvolvimento ao final. Mesma conclusão técnica da rodada
  NVIDIA anterior: **a AMD também não publica API pública suportada para
  escrever no perfil por aplicativo do Adrenalin**, então quase toda a
  lista 🟡 (Anti-Lag, Chill, Boost, Image Sharpening, Radeon Super
  Resolution, Enhanced Sync, limite de FPS, perfil por app, desativar
  overlay) e o item 🧪 (AMD Fluid Motion Frames) caíram em 🚫 pelo mesmo
  motivo já documentado — não é uma decisão nova, é a mesma política
  aplicada de forma consistente contra o novo fabricante.
- **Nenhuma ação nova de catálogo desta vez** (`CurrentVersion` continua
  `12`) — o que dava para implementar já existia como infraestrutura
  genérica de fabricante das rodadas anteriores; só precisou generalizar:
  - `windows.gaming.gsync.guide`/`GSyncGuidanceDiagnosisAction` ganhou uma
    dependência de `IGpuVendorInspector` e agora nomeia o painel certo
    conforme o fabricante detectado ("NVIDIA Control Panel (Configurar
    G-SYNC)" vs. "AMD Software: Adrenalin Edition (FreeSync)"),
    generalizando o cobertura para incluir FreeSync. Nome/descrição da
    ação no catálogo atualizados de "G-SYNC/VRR" para "G-SYNC/FreeSync/VRR".
  - `GpuVendorDetectionAction.Classify` ganhou links de download por
    fabricante detectado (nvidia.com/drivers, drivers.amd.com, Intel
    download center) — cobre "direcionar ao driver oficial" para os três
    fabricantes de uma vez.
  - `GuidedDriverReinstallAction` e `DriverVersionsDiagnosisAction`
    (versão/idade do driver) já eram vendor-neutros desde a rodada
    anterior — só o texto do primeiro foi ajustado para mencionar
    explicitamente que vale para AMD e NVIDIA.
- **Testes novos/ajustados**: `GSyncGuidance_NamesAmdSoftwareForRadeonGpus`
  (novo); `GSyncGuidance_SuggestsFpsCapBelowMaxRefreshWhenKnown`/
  `GSyncGuidance_StillOrientsWhenRefreshRateIsUnavailable` atualizados para
  a nova assinatura de `Classify` (recebe `GpuVendorSnapshot`);
  `GpuVendorDetection_ClassifiesKnownVendorsAndNeverWritesAnything`
  ampliado para verificar os links de download.
- Validação: `dotnet build`/`dotnet test` Release (526 testes, eram 525);
  `scripts\Verify-Safety.ps1` aprovado.

## Implementação do lote "Driver Intel e notebooks híbridos" (26/07/2026, sexta rodada)

- O usuário mandou o lote Intel/híbridos já pedindo implementação direta e
  push ao final. Catálogo subiu de `CurrentVersion = 12` para `13` com 1
  ação nova; a maior parte do lote **já estava coberta** por
  infraestrutura das rodadas anteriores (GPU vendor detection, GPU
  preference, driver version/age, links de download).
- **Nova ação**: `windows.gaming.hybrid-laptop.diagnose` (👁, todos os
  perfis, `ActionOptionGate.Always`) — `HybridLaptopDiagnosisAction` em
  `DiagnosticActions.cs`. Combina:
  - `IPowerStatusProvider.IsBatterySaverActive()` (novo método na
    interface, lê o bit 0 de `SystemStatusFlag` do `GetSystemPowerStatus`
    já usado por `IsOnAcPower()`) + o estado de CA/bateria já existente —
    cobre "detectar notebook em modo economia" e "recomendar conectar
    carregador".
  - `IVendorLaptopSoftwareInspector`/`WindowsVendorLaptopSoftwareInspector`
    (novo, em `VendorLaptopSoftwareInspector.cs`) — detecta, via
    enumeração read-only do registro de desinstalação (mesmo padrão do
    `StreamingSoftwareDetector`), se um utilitário conhecido de
    troca de GPU/desempenho do fabricante do notebook está instalado
    (Armoury Crate, MSI Center, Dragon Center, Lenovo Vantage, Dell Power
    Manager, Alienware Command Center, HP Omen Gaming Hub, Acer
    PredatorSense, Gigabyte Control Center) — cobre "detectar MUX switch
    quando exposto pelo fabricante" (como proxy honesto: detecta a
    ferramenta que controlaria o MUX, nunca afirma que o MUX em si
    existe) e "recomendar modo GPU dedicada no software do notebook".
- **Já estava coberto, sem mudança de código**: "detectar Intel Arc ou
  integrada" (a mesma heurística de marcadores já distingue Intel
  integrada de qualquer GPU dedicada, incluindo Arc, que não bate com os
  marcadores de integrada); "detectar driver"/"direcionar ao driver
  oficial" (diagnóstico de driver e links por fabricante, já vendor-neutros
  desde a rodada NVIDIA, já incluíam Intel); "forçar FiveM para a GPU
  dedicada pelo Windows" (`windows.gaming.high-performance-gpu.prefer`,
  já implementado há várias sessões); "detectar limite térmico ou de
  potência" (`safety.throttling-signal.diagnose`/`safety.thermal.diagnose`,
  não duplicados pela ação nova).
- **Classificado 🚫 (mesma razão das rodadas NVIDIA/AMD)**: "ativar perfil
  Performance do fabricante" — os utilitários de notebook (Armoury Crate,
  MSI Center, Lenovo Vantage etc.) também não publicam API pública
  suportada para ativar perfis de desempenho de fora do próprio app; e a
  regra já vigente de nunca controlar MUX/BIOS por método genérico não
  documentado, confirmada mais uma vez.
- **Testes novos**: `WindowsVendorLaptopSoftwareInspector_NeverThrows`,
  `WindowsPowerStatusProvider_IsBatterySaverActive_NeverThrows`,
  `HybridLaptopDiagnosis_RecommendsChargerAndBatterySaverWhenOnBattery`,
  `HybridLaptopDiagnosis_MentionsDetectedVendorToolOnAc`
  (`HardwareDiagnosticActionsTests.cs`); listas de ação esperadas em
  `LightProfile_UsesOnlyLowImpactStandardUserActions`/
  `DisabledOptions_RemoveTheirActionsButKeepSafetyPreflight` atualizadas.
- Validação: `dotnet build`/`dotnet test` Release (530 testes, eram 526);
  `scripts\Verify-Safety.ps1` aprovado.

## Implementação parcial do lote "Energia e CPU" (26/07/2026, sétima rodada) — decisão de arquitetura pendente

- O usuário mandou um lote grande sobre plano de energia próprio,
  prioridade de processo, afinidade de CPU, core parking, timer
  resolution e polling rate de mouse, pedindo implementação direta.
- **Decisão central desta rodada, importante para sessões futuras**: a
  maior parte deste lote (plano de energia "ativado só durante a sessão e
  restaurado ao fechar o FiveM", prioridade de processo "testada e
  restaurada ao fechar", afinidade de CPU, core parking, timer resolution
  "enquanto o jogo estiver aberto") **pressupõe um processo de vigilância
  de ciclo de vida do FiveM/GTA V (detectar início e fim em tempo real)
  que este produto não tem hoje**. O FiveMCleaner é uma ferramenta
  transacional de "aplicar uma vez, verificar, confirmar, reverter se
  necessário" — não existe um serviço/watcher residente que aplique algo
  quando o jogo abre e desfaça quando ele fecha.
- Implementar esses itens de forma incompleta (ex.: subir a prioridade do
  processo do GTA sem qualquer garantia de restaurá-la quando o jogo
  fechar, possivelmente com o FiveMCleaner já encerrado) quebraria o
  princípio de segurança central do projeto — toda ação reversível precisa
  de um caminho garantido de reversão. Por isso, **esses itens não foram
  implementados** e ficaram documentados como **decisão de arquitetura
  pendente** em `docs/graphics-optimizations-backlog.md` (seção 13),
  com uma recomendação explícita: antes de portá-los para o catálogo, uma
  sessão futura precisa decidir e documentar em `docs/architecture.md`
  como o app vai detectar o ciclo de vida do FiveM/GTA V em tempo real e
  garantir reversão mesmo se o FiveMCleaner for fechado antes do jogo.
- **O que foi implementado, por caber no modelo transacional atual (ajuste
  único, reversível, sem depender de vigilância contínua)**: catálogo
  subiu de `CurrentVersion = 13` para `14` com 2 ações novas.
  - `windows.power.pcie-aspm.adjust` (✅, Médio e Agressivo,
    `PciExpressPowerManagementAction`): desativa o PCI Express Link State
    Power Management (ASPM) do plano de energia ativo via `powercfg /Q` +
    `/setacvalueindex`/`/setdcvalueindex` (mesmo mecanismo documentado já
    usado pela ação de plano de energia), reduzindo picos de latência.
    Totalmente reversível; se o computador não expõe essa configuração ou
    a leitura do texto do `powercfg` não bate (varia por idioma do
    Windows), a ação simplesmente não faz nada — nunca falha, nunca
    quebra a transação.
  - `windows.gaming.mouse-polling-rate.guide` (👁, todos os perfis,
    `MousePollingRateGuidanceAction`): quando a CPU está sob carga alta
    (reaproveitando `IResourceUsageInspector`, mesma medição já usada nos
    diagnósticos existentes), orienta testar reduzir mouses de 4000/8000
    Hz para 1000 Hz. Documentado explicitamente que este app **não
    consegue ler a taxa de polling real do mouse** (sem API pública para
    isso) nem correlacionar stutter com movimento de mouse em tempo real —
    a orientação é sempre condicionada à carga de CPU observada, nunca
    uma afirmação de fato sobre o mouse do usuário.
- **Já coberto, sem mudança de código**: "não manter CPU em 100%
  permanentemente"/"não usar Ultimate Performance como religião oficial"
  (o plano de alto desempenho já é sempre temporário e reversível); as
  regras de nunca usar Realtime, nunca elevar processos indiscriminadamente,
  nunca reduzir processos essenciais, nunca desativar CPU 0/SMT
  automaticamente, nunca editar registro permanentemente — todas já eram a
  postura do produto, confirmadas aqui contra a lista específica, sem
  precisar de código novo.
- **Testes novos**: `GetPciExpressAspmPolicyAsync_ParsesTheCurrentAcValueFromPowercfgQuery`,
  `GetPciExpressAspmPolicyAsync_ReturnsNullWhenPowercfgFails`,
  `GetPciExpressAspmPolicyAsync_ReturnsNullWhenOutputFormatIsUnrecognized`,
  `TrySetPciExpressAspmPolicyAsync_SetsBothAcAndDcThenAppliesTheScheme`,
  `TrySetPciExpressAspmPolicyAsync_ReturnsFalseWhenPowercfgFails`,
  `TrySetPciExpressAspmPolicyAsync_RejectsOutOfRangeValues`
  (`PowerCfgControllerTests.cs`); `PciExpressPowerManagement_SetsOffAndRollbackRestoresPreviousPolicy`,
  `PciExpressPowerManagement_NoChangeWhenAlreadyOff`,
  `PciExpressPowerManagement_NoChangeWhenNotExposed`,
  `PciExpressPowerManagement_NoChangeWhenSetFails`,
  `MousePollingRateGuidance_MentionsHighCpuLoadAndCpuPercentAboveThreshold`,
  `MousePollingRateGuidance_StillOrientsWhenCpuIsNotUnderHeavyLoad`,
  `MousePollingRateGuidance_HandlesMissingCpuReadingGracefully`
  (`WindowsActionHandlerTests.cs`); listas de ação esperadas em
  `PlanBuilderTests.cs` atualizadas.
- Validação: `dotnet build`/`dotnet test` Release (543 testes, eram 530);
  `scripts\Verify-Safety.ps1` aprovado.

## Simplificação da barra inferior, dica visual de UAC e idioma Espanhol (27/07/2026)

- **Barra de resumo/ação (rodapé do Otimizador)**: removido o texto
  "N ações verificadas • sem elevação" e "Limpezas permanentes aparecem
  identificadas no plano. Configurações possuem rollback." do `Border` que
  fica logo acima dos botões "Revisar plano"/"Otimizar agora" em
  `MainWindow.xaml`. O card passou a conter apenas os dois botões,
  alinhados à direita, com padding maior — aplica-se aos três perfis
  (Leve/Médio/Agressivo) porque é um único controle compartilhado, não
  marcação por perfil. As propriedades de view-model que alimentavam esse
  texto (`PlanSummary`, `ElevationLabel`, `SelectedActionCount`) foram
  mantidas (não são usadas por nenhum teste e removê-las seria além do
  escopo pedido), apenas o binding XAML foi retirado.
- **Dica de possível UAC**: adicionado um ícone minimalista "i" circulado
  (glifo Segoe MDL2 Assets `&#xE946;`, o mesmo já usado como ícone de
  fallback em outro lugar do catálogo de ações) ao lado do nome do perfil,
  apenas nos cartões Médio (Balanced) e Agressivo (Aggressive) — nunca no
  Leve, que não tem nenhuma ação administrativa. O `ToolTip` usa a nova
  chave `Profiles.MayRequireElevation` ("Pode requisitar confirmação de
  Administrador do Windows (UAC)" / "May request Windows Administrator
  confirmation (UAC)" / "Puede solicitar confirmación de Administrador de
  Windows (UAC)"), adicionada aos três catálogos de recursos.
- **Suporte a Espanhol**: terceiro idioma de interface, ao lado de
  Português (Brasil) e Inglês.
  - `LocalizationModels.cs`: `Spanish` adicionado a `AppLanguage` e
    `AppLanguagePreference`.
  - `LocalizationService.cs`: nova `SpanishCulture` (`CultureInfo.GetCultureInfo("es")`,
    cultura neutra — não uma variante regional como `es-ES`, para casar
    com o padrão de fallback do `ResourceManager`), entrada em
    `LanguageOptions`, ramo em `DetectLanguage` (ISO `"es"` → Spanish),
    `Resolve`, `SetLanguage` e `CultureFor`.
  - `MainViewModel.cs`: nova propriedade `IsSpanishSelected`; `SelectLanguage`
    ganhou o ramo `AppLanguage.Spanish => AppLanguagePreference.Spanish`;
    os dois pontos que notificavam `IsPortugueseSelected` via
    `OnPropertyChanged` agora também notificam `IsSpanishSelected`.
  - `MainWindow.xaml`: terceiro `ComboBoxItem` (`Tag="es"`) no
    `LanguageSelector`, usando a nova chave `Settings.Language.Spanish`.
  - `MainWindow.xaml.cs`: `LanguageSelector_SelectionChanged` passou de
    ternário binário para `switch` de três ramos por `Tag`
    (`pt-BR`/`es`/padrão inglês); a inicialização de `SelectedIndex`
    também passou a considerar os três casos (0=pt-BR, 1=en, 2=es,
    consistente com a ordem dos itens no XAML).
  - Novo arquivo `src/FiveMCleaner.App/Resources/Strings.es.resx`,
    espelhando integralmente as ~450 chaves de `Strings.resx` com tradução
    completa para o espanhol (nenhuma chave ausente — coberto pelo teste
    `EnglishAndSpanishCatalogs_HaveExactlyTheSameKeys`, que segue o mesmo
    padrão já existente para português). O `.resx` não precisou ser
    referenciado manualmente no `.csproj`: o SDK do WPF já faz glob
    automático dos `Strings.*.resx` como recurso satélite, do mesmo jeito
    que já acontecia com `Strings.pt-BR.resx`.
- **Testes novos/ajustados**: `LocalizedInterfaceContractTests.cs` passou a
  também instanciar um `LocalizationService` em `es` e verificar que toda
  chave usada em XAML/code-behind (`[Key]`, `T("Key")`/`F("Key")`) resolve
  nessa cultura, incluindo o teste de duplicidade de chaves nos `.resx`
  (agora cobre `Strings.es.resx` também) e o teste de nome/descrição de
  cada ação do catálogo. Em `LocalizationServiceTests.cs`: `es-ES`/`es-MX`
  passaram a esperar `AppLanguage.Spanish` na detecção automática (antes
  caíam no fallback inglês); novo teste
  `EnglishAndSpanishCatalogs_HaveExactlyTheSameKeys` (paridade de chaves);
  novo teste `SetLanguage_Spanish_UpdatesCultureAndPreference`; novo teste
  `SpanishLanguagePreference_RoundTripsThroughSettingsJson` (serialização
  JSON do enum).
- Validação: `dotnet build`/`dotnet test` Release (547 testes, eram 543);
  `scripts\Verify-Safety.ps1` aprovado.

## Rodada de robustez: elos fracos, bugs e limites reais (27/07/2026)

Rodada dedicada a caçar fragilidades em vez de adicionar funcionalidade. O
app foi executado (`--demo-synthetic --capture=`) antes e depois para
confirmar que continua iniciando e renderizando.

- **Crash ao abrir link/pasta externa (bug real, alto impacto)**:
  `App.xaml.cs` trata `DispatcherUnhandledException` chamando `Shutdown(1)`,
  ou seja, qualquer exceção não tratada na UI **encerra o aplicativo**. Três
  handlers de `MainWindow.xaml.cs` chamavam `Process.Start` sem proteção:
  `OpenReleaseNotes_Click` ("Ver alterações"), `OpenRepository_Click`
  ("Abrir GitHub") e `OpenLogs_Click` ("Abrir pasta de logs" — que ainda
  fazia `Directory.CreateDirectory` fora de try). Sem navegador padrão
  registrado, com a policy do Windows bloqueando o verbo, ou com acesso
  negado à pasta, clicar nesses botões fechava o FiveMCleaner inteiro.
  Agora existe um único helper `TryOpenExternal(Action)` que captura a falha
  e mostra um aviso localizado (novas chaves `Dialog.OpenExternal.Title` e
  `Dialog.OpenExternal.Message` nos três catálogos).
- **`ProcessCommandRunner` podia travar indefinidamente (elo fraco)**: as
  leituras de stdout/stderr usavam o `cancellationToken` do chamador, não o
  token com timeout. Quando um processo filho repassa o handle de saída para
  um neto que sobrevive a ele, o `await outputTask` ficava bloqueado até o
  neto morrer — ignorando completamente o timeout solicitado. Além disso, no
  caminho de timeout/cancelamento as duas tasks de leitura eram simplesmente
  abandonadas, virando *unobserved task exceptions*. Agora as leituras usam
  o mesmo `linkedSource` do `WaitForExitAsync`, e o caminho de erro observa
  ambas via `ObserveAsync` antes de propagar.
- **Limpeza em `finally` mascarando sucesso (elo fraco, 3 ocorrências)**:
  `JsonWindowsTransactionJournalStore.SaveAsync`,
  `ElevatedBrokerClient.WriteRequestAsync` e
  `AppOptimizationService.SaveSettingsAsync` gravavam em arquivo temporário,
  faziam `File.Move` e então, no `finally`, chamavam `File.Delete` **sem
  proteção**. Como antivírus costuma segurar o handle de um arquivo recém
  gravado, uma falha de limpeza transformava uma gravação já concluída e
  durável em exceção. No caso do journal isso é especialmente grave: o
  journal é o que torna o rollback possível, e o motor passaria a acreditar
  que não pode reverter uma transação que na verdade pode. Os três agora
  usam limpeza best-effort.
- **Fila de telemetria reenviando o mesmo evento para sempre (bug real)**:
  `LocalTelemetryQueue.TryDelete` capturava apenas `IOException`, mas
  `File.Delete` lança `UnauthorizedAccessException` (que **não** é
  `IOException`) para arquivo somente-leitura ou com ACL negando acesso.
  Como `Remove()` roda logo após um envio **bem-sucedido**, um único arquivo
  não deletável escapava da exceção e fazia o mesmo evento voltar em todo
  flush seguinte, indefinidamente.
- **Fila de telemetria sem limite real (ponto simples → mais trabalhado)**:
  `PurgeOlderThan` limitava só por idade (14 dias). Idade sozinha não é um
  limite: cada execução enfileira eventos, mas um flush drena apenas um lote
  (`MaxBatchSize = 20`), então um período offline prolongado fazia a fila
  crescer durante toda a janela de retenção sem nada para contê-la. Foi
  introduzido `Prune(maxAge, maxFiles)`, que limita por idade **e** por
  contagem (`MaxQueuedEvents = 200`), descartando os mais antigos primeiro
  (nomes têm prefixo de timestamp, então ordem ordinal é cronológica).
  `PurgeOlderThan` permanece como atalho compatível.
- **Testes novos**: novo arquivo `ProcessCommandRunnerTests.cs` —
  `RunAsync_ReturnsOutputAndExitCodeForACommandThatCompletes`,
  `RunAsync_KillsAndReportsATimeoutWhenTheProcessNeverExits`,
  `RunAsync_TimesOutInsteadOfHangingWhenAGrandchildInheritsTheOutputPipe`
  (regressão real: com o código antigo a chamada levava ~30s e retornava
  sucesso em vez de respeitar o timeout de 2s),
  `RunAsync_PropagatesCallerCancellationRatherThanATimeout`,
  `RunAsync_RejectsExecutablesThatAreNotFullyQualified`. Em
  `CloudflareTelemetryServiceTests.cs`:
  `Prune_DropsTheOldestEventsOnceTheCountCeilingIsExceeded`,
  `Prune_KeepsEverythingWhenTheQueueIsWithinBothBounds` e
  `Remove_DoesNotThrowWhenTheQueuedFileCannotBeDeleted`.
- **Verificado e considerado saudável, sem mudança**: `AddLog` já limita o
  log de atividade a 100 itens; os métodos `async` do `MainViewModel` já
  capturam exceções internamente (os `async void` do code-behind não ficam
  desprotegidos por isso); `CancelOptimization` e o `finally` que descarta o
  `CancellationTokenSource` rodam ambos na thread de UI, sem corrida.
- Validação: `dotnet build`/`dotnet test` Release (555 testes, eram 547);
  `scripts\Verify-Safety.ps1` aprovado; app iniciado e renderizado antes e
  depois das mudanças.

## Atualizador automático de um clique (27/07/2026)

Substitui o fluxo antigo ("Baixar atualização" → confirmar → o instalador
abre visivelmente com wizard completo → app fecha) por um fluxo de um único
clique: o usuário clica em "Atualizar agora", confirma uma vez, e a partir
daí tudo acontece sozinho — download verificado, instalação totalmente
silenciosa (sem wizard, sem reaceitar termos), fechamento do app e reabertura
automática já na versão nova. Implementa a "primeira etapa" recomendada
(instalador atual em modo silencioso), deixando a "segunda etapa" (um
`FiveMCleaner.Updater.exe` próprio com atualização incremental, canais
estável/beta, download retomável) como evolução futura documentada mas não
construída agora — não havia necessidade de um segundo executável só para
isto, e um updater separado "com 48 bibliotecas" seria complexidade
prematura para o que o Inno Setup já resolve em modo silencioso.

- **`installer/FiveMCleaner.iss`**: nova entrada `[Run]` condicionada por
  uma função Pascal `IsAutomaticUpdateRelaunch()`, que só retorna
  verdadeiro quando a instalação é silenciosa (`WizardSilent`) **e** foi
  iniciada explicitamente com `/AUTOUPDATE=yes`. Só nesse caso o instalador
  reabre `{app}\FiveMCleaner.exe --updated={#AppVersion}` ao final. Qualquer
  outra instalação silenciosa (ex.: deploy administrativo via
  `/VERYSILENT` sem essa flag) continua sem abrir nada, preservando o
  comportamento silencioso padrão do Inno Setup. A entrada `postinstall
  skipifsilent` original (abrir o app ao final de uma instalação manual)
  continua intacta e sem relação com esta.
- **`scripts/Verify-Installer.ps1`**: três novos padrões obrigatórios no
  contrato do instalador — `Check: IsAutomaticUpdateRelaunch`, o gate
  `WizardSilent and ... {param:AUTOUPDATE|no}` (garante que o flag precisa
  ser passado explicitamente, nunca assumido) e a entrada `Parameters:
  "--updated=` apontando para o próprio `{app}\{#AppExeName}`. O `.iss` foi
  compilado de ponta a ponta com `ISCC.exe` contra o publish existente para
  confirmar que o Pascal Script novo não quebra a compilação.
- **Novo `src/FiveMCleaner.App/Services/SilentUpdateInstaller.cs`**: executa
  o instalador já baixado e verificado por SHA-256
  (`GitHubReleaseUpdateService.DownloadUpdateAsync`) com
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /AUTOUPDATE=yes`
  (mais `/LOG=` opcional). Duas proteções de segurança antes de executar
  qualquer coisa: (1) o caminho do instalador precisa estar contido dentro
  da própria pasta de atualizações verificadas (mesmo padrão de
  `GetContainedPath` já usado no download) — um valor manipulado nunca vira
  um "execute qualquer coisa"; (2) precisa terminar em `.exe` e existir de
  fato no disco. Depois de iniciar o processo, aguarda uma janela de
  "assentamento" de 4s (`SettleWindow`): a maioria dos problemas de startup
  do Inno Setup (mutex de instalação concorrente, payload corrompido, pasta
  sem permissão de escrita) aparece como saída quase imediata com código
  diferente de zero — capturar isso é o que permite ao app **continuar
  aberto e explicar a falha** em vez de fechar rumo a uma atualização que
  nunca vai acontecer. Se o processo ainda está rodando ao fim da janela,
  isso é o caminho normal: o app fecha e o `[Run]` do instalador cuida da
  reabertura. Interface `IInstallerProcessLauncher`/`IInstallerProcess`
  isola o `Process.Start` real para permitir teste sem instalar nada de
  verdade.
- **`MainViewModel.cs`**: novo estado `UpdatePresentationState.Installing`;
  `DownloadAndInstallUpdateAsync()` (baixa e instala em sequência) e
  `InstallDownloadedUpdateAsync(DownloadedUpdate)` (só instala, para retry
  sem baixar de novo); `CanDownloadUpdate` passou a exigir também `!IsBusy`
  — **atualizar substitui o executável em execução, então nunca deve ficar
  disponível enquanto uma otimização, rollback ou gravação de configurações
  estiver em andamento**, já que isso abandonaria a operação pela metade
  sem forma segura de terminar ou reverter; `ReportCompletedUpdate(string)`,
  chamado uma única vez na inicialização quando o processo foi relançado
  pelo próprio instalador (`--updated=X.Y.Z`), mostra o banner de
  confirmação "FiveMCleaner atualizado para a versão X.Y.Z" em vez do
  banner de "atualização disponível". `RaiseCommandState()` agora também
  notifica `CanDownloadUpdate`, então o botão reage imediatamente a
  qualquer mudança de `IsBusy`.
- **`MainWindow.xaml.cs`**: lê `--updated=<versão>` na linha de comando e
  chama `viewModel.ReportCompletedUpdate(...)` antes do primeiro
  `MainWindow_Loaded`. `DownloadUpdate_Click` virou o clique único
  completo: confirma uma vez (mensagem já deixa claro que o app vai
  fechar e reabrir sozinho, sem instalador visível), chama
  `DownloadAndInstallUpdateAsync()`, e só fecha a janela se a instalação
  silenciosa realmente começou — uma falha em qualquer etapa mantém o app
  aberto com o banner explicando o problema, nunca fecha "no escuro".
- **`MainWindow.xaml`**: o botão de ação do banner de atualização some
  inteiramente (`IsUpdateActionVisible`) na tela de confirmação pós-update,
  já que não há mais nada a fazer; ganhou um `ToolTip` explicando por que
  está desabilitado enquanto ocupado.
- **Textos novos, nos três catálogos** (`Strings.resx`, `Strings.pt-BR.resx`,
  `Strings.es.resx`): `Update.InstallNow`, `Update.Installing.Title/Detail`,
  `Update.Completed.Title/Detail`, `Update.BlockedWhileBusy`,
  `Log.UpdateInstallStarted`, `Log.UpdateInstallFailed`,
  `Log.UpdateCompleted`. `Dialog.UpdateInstall.Message/Failed` foram
  reescritos para descrever o comportamento novo (instalação silenciosa e
  automática, não mais "abrir o instalador"). As chaves `Update.Download` e
  `Update.OpenInstaller`, que descreviam o fluxo antigo de dois cliques e
  ficaram sem nenhuma referência em código após esta mudança, foram
  removidas dos três catálogos.
- **Testes novos**: `SilentUpdateInstallerTests.cs` (contrato de argumentos,
  rejeição de caminho fora da pasta de atualizações, rejeição de arquivo
  não-`.exe`, rejeição de arquivo inexistente, mensagens de erro nunca
  vazias para os códigos de saída documentados do Inno Setup, validação de
  pasta relativa); `MainViewModelAutoUpdateTests.cs` (fluxo completo de
  sucesso, falha do instalador mantém o banner visível e retorna false,
  ausência de instalador configurado não lança exceção, exceção do launcher
  é capturada, e — o teste mais importante desta rodada —
  `CanDownloadUpdate` fica `false` durante um rollback em andamento e volta
  a `true` ao terminar, usando um `IAppOptimizationService` fake com
  `TaskCompletionSource` para observar `IsBusy` no meio da operação).
  `FakeReleaseUpdateService` e `FakeSilentUpdateInstaller` (novo) permitem
  orquestrar os cenários sem rede real ou processo real.
- **Decisão de arquitetura registrada para o futuro**: a "segunda etapa"
  do atualizador (executável dedicado `FiveMCleaner.Updater.exe`,
  atualização incremental, rollback de atualização, canais estável/beta,
  download retomável, reparo de instalação) não foi implementada nesta
  rodada. O modo silencioso do Inno Setup já entrega toda a experiência
  pedida (um clique, sem wizard, reabertura automática) sem introduzir um
  segundo processo com seu próprio ciclo de vida, telemetria e superfície
  de bugs.
- Validação: `dotnet build`/`dotnet test` Release (570 testes, eram 555);
  `scripts\Verify-Installer.ps1 -ScriptOnly` aprovado (incluindo compilação
  real do `.iss` via `ISCC.exe` contra o publish existente);
  `scripts\Verify-Safety.ps1` aprovado; app iniciado e renderizado antes e
  depois das mudanças. Nenhum arquivo de versão, `CHANGELOG.md` ou artefato
  de distribuição foi alterado — por `AI_RULES.md`, isso só acontece durante
  uma publicação oficial explicitamente solicitada, não neste push de
  desenvolvimento.
- **Verificação real, ponta a ponta, de atalho e inicialização com o
  Windows sobrevivendo à atualização silenciosa**: dúvida levantada pelo
  usuário logo após esta rodada — a atualização automática também precisa
  preservar o atalho da área de trabalho e a preferência "iniciar com o
  Windows" quando o usuário já tinha ativado, sem duplicar nem perder nada.
  Em vez de responder por dedução, foi feito um teste manual real e
  completo, fora do repositório (pasta temporária, nunca a instalação real
  da máquina): (1) instalação silenciosa com `/TASKS=desktopicon,startup`,
  confirmando que a chave `HKCU\...\Run\FiveMCleaner` foi criada com
  `"<app>\FiveMCleaner.exe" --startup`; (2) execução dos **argumentos
  exatos** que `SilentUpdateInstaller.BuildArguments()` produz
  (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /AUTOUPDATE=yes`,
  sem `/TASKS`) simulando a atualização automática de verdade; (3)
  confirmação de que a chave de registro permaneceu **idêntica** depois —
  nem duplicada, nem removida. Isso funciona por três configurações já
  existentes no `.iss` e não relacionadas a esta rodada:
  `UsePreviousTasks=yes` (reaplica as tarefas escolhidas antes — atalho de
  área de trabalho e início com o Windows — sem perguntar de novo, já que
  `/VERYSILENT` nunca mostraria essa página de qualquer forma),
  `UsePreviousAppDir=yes` e `UsePreviousGroup=yes` (a atualização reinstala
  exatamente no mesmo caminho, então o atalho e a entrada do Registro
  continuam apontando para o lugar certo em vez de quebrar ou duplicar).
  Confirmado também que o próprio app relança de verdade após a instalação
  silenciosa (processo `FiveMCleaner.exe` observado em execução via
  `tasklist` logo após o passo 2). Instalação de teste, atalhos, grupo do
  Menu Iniciar e a chave de registro de teste foram completamente
  desinstalados e removidos ao final; a pasta `%LocalAppData%\FiveMCleaner`
  real do desenvolvedor (compartilhada entre todas as instalações pelo
  nome do produto, não pelo caminho) foi checada e confirmada intacta —
  nenhum arquivo novo ou modificado por este teste.

## Preparação da publicação v1.1.0 (27/07/2026)

- O conjunto acumulado desde `v1.0.3` foi classificado como **minor**: ele
  adiciona capacidades públicas compatíveis — consentimento de privacidade,
  telemetria/relatos pelo Worker Cloudflare, atualização silenciosa de um
  clique, novos diagnósticos/ações e idioma Espanhol — sem quebrar o contrato
  de instalação existente. A próxima versão estável é `v1.1.0`.
- `Directory.Build.props`, o fallback do instalador, README, changelog,
  telemetria de exemplo e a central de download foram alinhados para `1.1.0`.
  A página pública resume somente mudanças efetivamente presentes nesta versão.
- `Test-PublicVersionProgression.ps1` deixou de impor erroneamente somente o
  próximo patch. Ele aceita exclusivamente o próximo incremento SemVer válido
  de patch, minor ou major; a classificação continua sendo responsabilidade da
  revisão de release, conforme `AI_RULES.md`.
- A PR automática do Dependabot que atualizava seis ações oficiais do GitHub
  (checkout, setup .NET, Pages e atestação) foi revisada e integrada antes da
  tag final, para que a própria release `v1.1.0` use a cadeia de CI atualizada.
- Validação local de release: progressão `v1.0.3 → v1.1.0` aprovada; build
  Release sem avisos; 570 testes .NET aprovados; `Verify-Safety.ps1` e contrato
  do instalador aprovados; lint, typecheck, build e 3 testes do site aprovados;
  104 testes do Worker e 35 do painel administrativo aprovados.
