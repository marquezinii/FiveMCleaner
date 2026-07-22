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
  `docs/architecture.md`. O catálogo de ações está na versão 3
  (`ActionCatalog.CurrentVersion`).
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

## Funcionalidades presentes no código atual

- Diagnóstico de FiveM Legacy, GTA, CPU, GPU, memória, armazenamento, cache e
  processos relevantes.
- Modos de otimização Leve, Médio e Agressivo, escolhidos apenas pelo modo (o
  usuário nunca marca tweaks individuais); a `MainViewModel` deriva as opções
  técnicas do perfil selecionado e do diagnóstico.
- Ações reversíveis e restritas para configurações gráficas Legacy, Game Mode,
  preferências de GPU, energia de sessão, captura em segundo plano, efeitos
  visuais e limpezas condicionadas.
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
- O instalador público e o sistema de atualização automática continuam fora
  do escopo desta etapa, conforme combinado; ficam para uma tarefa futura.
- O build, lint e testes renderizados do site passam, mas `npx tsc --noEmit`
  atualmente reporta tipos ausentes do runtime Cloudflare (`cloudflare:workers`,
  `Fetcher` e `D1Database`). Isso deve ser tratado como uma limitação conhecida
  do site, não mascarado por uma cadeia de comandos.
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
- Última validação do app: `dotnet build` Release sem avisos/erros, **235
  testes .NET aprovados** (215 anteriores + 20 novos cobrindo isolamento,
  dependência, aborto crítico, outcome, relatório, sanitização e apresentação
  de modos) e `scripts\Verify-Safety.ps1` aprovado. O executável real abriu e
  permaneceu estável por smoke test manual (`Start-Process` + `--demo-synthetic`,
  5s, sem novas entradas em `crash.log`) exercitando as novas telas de
  progresso/relatório/apresentação de modo.
- Área ainda não coberta por teste automatizado nesta etapa: a integração fim
  a fim `AppOptimizationService → runtime real do Windows` (só é exercitada
  por doubles no motor; o serviço de app em si depende de Windows real). É um
  bom próximo passo para o agente seguinte, se quiser reforçar cobertura.
- Última validação do site: lint, build e testes renderizados aprovados. O
  typecheck continua limitado pelos tipos ausentes do runtime Cloudflare,
  conforme descrito acima; não mascarar esse erro nem substituir por comandos
  que ignorem tipos.
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
