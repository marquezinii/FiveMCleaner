# Estado atual do projeto

> Documento canônico e deliberadamente curto. Ele descreve **o estado vigente**, não o histórico de implementação.
> Código, testes, Git e documentação especializada prevalecem se houver divergência. Para histórico detalhado, consulte `PROJECT_HISTORY.md` somente quando a tarefa realmente exigir contexto antigo.

## 1. Snapshot

- **Produto:** Ralven, plataforma de gerenciamento e otimização do Windows com IA, transparente, reversível e orientada por diagnóstico. FiveM para **GTAV Legacy** é a integração especializada atual da área de Jogos.
- **Integração:** `dev/proxima-versao` é a branch de integração da próxima versão; `main` representa a linha pública/estável. O fluxo de branches, worktrees, Pull Requests, integração e release é definido em `AI_RULES.md`.
- **Último estado consolidado:** 10/09/2026, após a reconciliação dos PRs #153–#177: posicionamento de plataforma Windows com IA, sistema visual unificado e compatível com Windows 10, monitoramento local ao vivo, conta/2FA, telemetria v9, dashboard de comando, Ralven AI com ações locais tipadas, release endurecida, instalador renovado, CI por escopo e localização verificável em quatro idiomas. Confirme o estado real com Git e testes atuais antes de trabalhar.
- **Release pública atual:** `v1.6.1`, publicada a partir de `main`. A próxima versão só é definida no fluxo oficial de release a partir das mudanças posteriores a essa tag.
- **Atalho de desenvolvimento:** `Ralven - Desenvolvimento` usa `scripts\Start-DevelopmentApp.ps1`. Conforme `AI_RULES.md`, deve ser reconstruído com `scripts\Install-DevelopmentShortcut.ps1 -Build` quando aplicável. O script espelha a árvore para a pasta irmã fixa `Ralven-dev-shortcut`, sem ficar órfão após a remoção de um worktree.

## 2. Objetivo e invariantes de segurança

- Priorizar mudanças pequenas, verificáveis, diagnosticáveis e reversíveis; nunca prometer ganho universal de FPS.
- A integração de **FiveM para GTAV Legacy** é a única integração de jogo com suporte operacional hoje. GTAV Enhanced deve ser detectado/bloqueado com segurança até existir suporte específico.
- Nunca desativar Defender, Firewall, SmartScreen, UAC, Windows Update ou serviços essenciais; nunca criar exclusões de antivírus.
- Nunca injetar código, alterar memória de processos, instalar driver de kernel, usar hook gráfico ou baixar/executar código arbitrário como mecanismo de otimização.
- Caches e arquivos sensíveis são tratados por allowlist. Autenticação, `game-storage`, NUI storage, configurações e plugins não são lixo automático.
- Perfis **Leve, Médio e Agressivo** e o plano pessoal **Ultra** são composições versionadas de ações; nunca uma lista arbitrária de tweaks.
- Cada ação deve ter escopo conhecido, pré-condições, validação, resultado tipado e rollback quando aplicável.
- O fluxo padrão é isolado por ação: verificar → aplicar → validar → registrar. Falha normal reverte somente a ação afetada; falha crítica pode abortar o restante. O broker elevado mantém contrato estrito e allowlisted. Cancelamentos preservam o histórico e os snapshots confirmados; a restauração desfaz a fase administrativa antes da local.
- Não medir FPS ao vivo dentro do FiveM por overlay/hook. O benchmark implementado é o benchmark **standalone oficial do GTA V**, opt-in e fora de uma sessão FiveM.
- Dados indisponíveis por limitações do Windows/driver devem aparecer como indisponíveis; nunca estimar ou inventar métricas.

Documentos normativos: `docs/safety.md` e `docs/architecture.md`.

## 3. Arquitetura atual

### Solução .NET

`Ralven.slnx` separa responsabilidades. A árvore de `src/` possui nove projetos principais:

- `Ralven.App` — WPF, navegação, localização, tema, conta, apresentação, progresso e interação.
- `Ralven.Contracts` — DTOs, IDs, enums e contratos compartilhados; os estados persistidos de transação e journal são contratos duráveis append-only.
- `Ralven.Core` — catálogo de ações, perfis, planejamento e regras independentes de Windows/UI; o planejamento é puro e recebe explicitamente suas entradas variáveis.
- `Ralven.Windows` — descoberta e adaptadores Windows, filesystem, registro, diagnósticos e ações permitidas.
- `Ralven.Broker` — processo administrativo efêmero e allowlisted; sem shell/comandos arbitrários.
- `Ralven.Launcher` — inicialização/ativação do runtime e coordenação do fluxo de atualização.
- `Ralven.Updater` — atualização independente e staging/aplicação da atualização.
- `Ralven.UpdateRuntime` — contratos/estado durável usados pela cadeia de atualização e recuperação.
- `Ralven.ReleaseTool` — suporte à preparação/validação de artefatos de release.

Testes .NET ficam em `tests/Ralven.Tests/`.

A toolchain integrada usa .NET 10 LTS com SDK 10.0.303, C# 14 fixo e NuGet Central Package Management em `Directory.Packages.props`. Os testes usam xUnit v3 sobre Microsoft Testing Platform, com cobertura via `coverlet.MTP`.

### Infraestrutura e web

- `infra/cloudflare-worker/` — backend Cloudflare Worker + D1 para telemetria, relatos de bug e perfil de conta.
- `infra/dashboard/` — painel administrativo privado da telemetria/bugs.
- `https://vemryx.com/Ralven/` — página pública e origem dos downloads; manifestos assinados e artefatos versionados são servidos pelo Worker do site a partir de R2 privado.
- `installer/` — Inno Setup 7 em arquitetura x64.
- `scripts/` — build, validação, release, smoke tests e launcher de desenvolvimento.
- `.github/workflows/` — CI de .NET/Worker/dashboard, SBOM e release. O site público vive no repositório Vemryx; Dependabot cobre NuGet, npm da infraestrutura e Actions.

Node 24.19 LTS é o baseline versionado para site, Worker e dashboard.

### Persistência local

Preferências, journals, solicitações efêmeras, filas e logs locais ficam sob `%LOCALAPPDATA%\Ralven`; não gravar dados mutáveis na pasta de instalação. Na primeira abertura, o importador allowlisted pode copiar dados pessoais compatíveis de gerações sem suporte, sem sobrescrever nem alterar a origem.

## 4. Estado funcional relevante

### Interface

- Aplicação WPF com WPF-UI/Fluent, Mica, tema claro/escuro/sistema e localização.
- A identidade pública descreve o Ralven como plataforma de gerenciamento e otimização do Windows com IA; FiveM/GTAV Legacy permanece uma integração especializada, não a definição do produto.
- Catálogos declarativos mantêm inglês, português do Brasil, espanhol e francês para App, Updater e camada Windows. O seletor usa o catálogo, segue o Windows quando automático e a CI recusa chaves, placeholders ou revisões de tradução divergentes; a pseudo-localização é usada para inspeção visual.
- Botões, switches, diálogos, cards e estados de configuração seguem os componentes compartilhados atuais. O shell preserva fallback visual em Windows 10 sem reduzir as proteções ou o comportamento de Windows 11.
- Janela principal inicia/restaura maximizada e preserva comportamento de bandeja.
- Configurações reúne inicialização/bandeja, aparência e idioma, privacidade, atualizações e conta; o idioma automático aparece como **Idioma do sistema**.
- Configurações também concentra restauração explícita de preferências, limpeza opt-in apenas de cache descartável do Ralven (com confirmação e preservação de conta, histórico, rollback e telemetria pendente), comportamento de bandeja e controles de aviso/atualização.
- Visão geral apresenta diagnóstico/prontidão e monitoramento local de recursos; a coleta só fica ativa quando a página está visível, a janela está ativa e não está minimizada, cancelando amostras em curso sem aplicar resultados antigos.
- O painel ao vivo mostra CPU, GPU, memória, disco e rede em gráficos acessíveis, com escala por métrica, status semântico e histórico local; não usa hook, injeção ou mede FPS dentro do FiveM.
- O hub de Jogos/FiveM concentra o monitor local e somente leitura da sessão FiveM; ele continua ativo na bandeja quando iniciado explicitamente, sem hook, leitura de memória ou mutação automática.
- Aba **Sistema** organiza diagnóstico essencial do PC, saúde agregada somente leitura de antivírus, firewall e atualizações automáticas da Central de Segurança do Windows e controles reais de jogos do Windows (Modo de Jogos, captura em segundo plano). Não abre superfícies externas; as duas alterações em HKCU allowlisted preservam snapshot/journal/rollback via `WindowsTransactionEngine` e refresh ao voltar para a página. Indisponibilidade da API é explícita e nunca vira afirmação de proteção.
- Aba **Aplicativos** apresenta três fluxos principais para usuários comuns — atualizar, instalar e desinstalar —, com detalhes técnicos (ID do pacote, origem) disponíveis sob demanda em "Mostrar detalhes técnicos" e inventário/atalhos nativos do Windows recolhidos em "Mais opções". O inventário não executa `UninstallString`, não altera `StartupApproved` nem escreve no Registro; o diálogo de confirmação sempre mostra nome, ID e origem antes de qualquer mutação.
- Aba **Jogos** é o catálogo de títulos compatíveis; hoje abre um hub dedicado do FiveM sobre GTAV Legacy, com acessos distintos ao otimizador especializado, ao otimizador geral, ao histórico/restauração, ao monitor local da sessão e ao download oficial do ReShade — sem redistribuir/executar instaladores, habilitar outros jogos ou GTAV Enhanced.
- O rodapé de navegação mostra somente a versão instalada em um card compacto; o status genérico de proteção foi removido.
- Página **Ralven Pro** localizada compara os planos Free e Pro em cartões equivalentes, com selo "Recomendado", benefícios reais do Pro (incluindo Ralven AI) e escolha explícita de continuar no Free; apresenta a oferta mensal (R$ 19,90) controlada pelo servidor, checkout hospedado (Asaas), atualização e cancelamento da assinatura; vendas permanecem desativadas (`ASAAS_BILLING_ENABLED = "false"`) até homologação. Ver `docs/billing.md` e `docs/asaas-setup.md`.
- Página **Ralven AI** oferece orientação contextual em linguagem natural a partir do diagnóstico/catálogo já conhecidos, com conversa somente em memória e revisão explícita do perfil recomendado pelo planejador transacional existente antes de qualquer aplicação. O acesso exige entitlement `ralven_ai` separado do Pro, e-mail verificado, flag explícita e orçamento server-side; a rota continua desativada até a ativação operacional. O modelo nunca recebe acesso ao Windows nem executa nada diretamente. Ver `docs/ralven-ai.md`.
- Revisão do plano do Otimizador detalha por ação: como é detectada, o que a confirmação verifica, como é desfeita e riscos/limitações; texto cai no conteúdo do catálogo quando a chave de localização não existe.
- Aba **Otimizador** oferece o plano geral `GeneralWindows` na trilha Preparar → Executar → Resultado e preserva a experiência especializada `FiveMLegacy` em Jogos. `OptimizerPage` (compartilhada pelos dois fluxos) usa hierarquia progressiva: benefício/impacto ficam na leitura principal; risco, acesso, verificação, rollback e limitações ficam sob detalhes técnicos expansíveis, sem remover conteúdo. O Ultra adiciona recomendação pessoal Pro, acompanhamento local opt-in e medições comparáveis limitadas; acesso é revalidado antes de novas operações.
- Nos perfis padrão, cache/reparo permanece opt-in: Leve limita mutações a limpeza temporária segura e Modo de Jogo; Médio adiciona captura, energia e ajustes moderados reversíveis; Agressivo adiciona somente o conjunto conservador de aparência/responsividade. O perfil FiveM mantém ações próprias de GTAV Legacy e bloqueia com segurança processos/sessões incompatíveis.
- Painel de **Notas da Versão** (`ReleaseNotesWindow`) é exibido automaticamente após um update bem-sucedido, controlado por `ReleaseNotesEvaluator`/`ReleaseNotesCatalog` e pelo campo `LastSeenReleaseNotesVersion` das configurações (mostra de novo só quando existem notas mais recentes que a última vista).
- Aviso ao vivo: ícone/banner no app consultam `GET /live-alert` (Worker) e mostram mensagem publicada pelo dashboard; dispensa é lembrada por `DismissedLiveAlertId` até o próximo aviso.
- `MainWindow.xaml.cs` e `MainViewModel.cs` são divididos em `partial class` por área de responsabilidade (ex.: `MainWindow.Navigation.xaml.cs`, `MainWindow.Capture.xaml.cs`, `MainViewModel.Progress.cs`, `MainViewModel.Settings.cs`); ao editar uma área, localize o arquivo parcial correspondente em vez de assumir um único arquivo monolítico.

### Motor de otimização e diagnóstico

- `ActionCatalog.CurrentVersion` mais recente registrado: **22**.
- Diagnósticos cobrem FiveM/GTA, CPU, GPU, RAM, armazenamento/TRIM, cache, processos, rede, pagefile/commit, drivers, taxa de atualização, aceleração do mouse, energia, WHEA, sinais de throttling e outros dados obtidos por APIs nativas/best-effort. Eventos WHEA no log `System` usam o provedor `Microsoft-Windows-WHEA-Logger`; rede não classifica gargalo a partir de contadores cumulativos de uma única leitura; RAM não infere canais/XMP/EXPO a partir de `Win32_PhysicalMemory`; VRAM considera o melhor adaptador conhecido em sistemas híbridos; pagefile é apresentado como limite/folga de commit, não como tamanho do arquivo. Ver `docs/research.md`.
- Existem diagnósticos somente leitura para gargalo provável, overlays/captura, logs do FiveM e orientação de medição pelas ferramentas oficiais do FiveM.
- Relatório estruturado e relatório técnico sanitizado podem ser copiados/salvos explicitamente pelo usuário.
- Falhas automáticas usam `BugCodeClassifier`; relatos manuais escolhem um motivo localizado mapeado para o mesmo `BugCode` allowlisted, permitindo agrupamento estável sem enviar classificação arbitrária.
- Journal, snapshots e rollback preservam rastreabilidade das ações; ações administrativas exigem um receipt autoritativo protegido em HKLM/Registry64 antes de permitir rollback, e receipt ausente/corrompido falha fechado. A revalidação de planos compara integralmente os metadados de ações e usa a reconstrução canônica da requisição.
- Ações XML de gráficos usam uma transação segura compartilhada; inspeção de processos e adaptadores de GPU têm primitivas de leitura separadas das mutações.
- A recomendação considera hardware, pressão de recursos, uso pretendido e software de transmissão. A resposta consistente do ponteiro é exclusiva do Ultra, opt-in, preserva a velocidade e restaura os três valores anteriores sem sobrescrever escolha posterior.
- Diagnóstico de criadores reconhece OBS, Streamlabs Desktop e TikTok LIVE Studio sem fechar processos nem inferir que uma live está ativa.

### Conta e autenticação

- Autenticação usa **Firebase Authentication REST** para cadastro, login, verificação, recuperação, reautenticação, alteração e exclusão de conta.
- O ID Token fica em memória; refresh token opcional é persistido protegido por DPAPI somente quando a escolha explícita de manter sessão permanece ativa em refresh/reautenticação. Logout só conclui após remover e verificar o estado persistido. O **Firebase UID** é o identificador interno permanente, nunca o e-mail.
- Perfil complementar (nome, sobrenome e username único) é armazenado no Worker/D1, indexado pelo UID autenticado.
- Worker valida ID Token Firebase por RS256/JWKS, incluindo `aud`, `iss`, expiração e `sub`.
- Login com Google usa OAuth2 + PKCE com redirect loopback.
- A sessão só é liberada após e-mail verificado, perfil existente e aceite da versão atual dos termos. O provedor Firebase determina se a conta possui senha; contas Google sem senha podem vinculá-la somente após reautenticação Google com o mesmo UID.
- Exclusão de conta remove o perfil Worker/D1 antes da conta Firebase e tenta compensar a remoção do perfil se a exclusão Firebase falhar.
- Segredos/configuração local de Google não são versionados; overlays `Config/appsettings.{Development,Production}.local.json` são git-ignorados.
- Avatar é normalizado e armazenado **somente localmente**; não existe backend de avatar.
- Card de conta mostra o plano (Free/Pro) lido de `GET /account/entitlements` (`CloudflareAccountEntitlementService`), autenticado pelo mesmo ID Token Firebase; nenhum dado de provedor de pagamento é exposto ao cliente.

### Telemetria, bugs e backend

- Infraestrutura registrada como ativa: `/telemetry`, `POST /bugs`, `GET /api/bugs` e `GET /live-alert`/`POST /admin/live-alert` (aviso ao vivo do dashboard para o app, painel dedicado no dashboard); relatos de bug são texto, e-mail opcional e trecho de log opcional. **Não há anexo/R2**.
- Telemetria e crash reporting obedecem consentimento e allowlists; falhas de envio nunca devem bloquear ou alterar o resultado da otimização. Após confirmar o aviso vigente, somente diagnósticos essenciais allowlisted são transmitidos; telemetria detalhada e crash reports sanitizados compartilham a opção **Relatórios opcionais**, habilitada por padrão em novas instalações e desativável a qualquer momento.
- Consentimento de privacidade na versão **9** (`PrivacyConsentPolicy`): além do `BugCode` fechado para classificação, ele separa o conjunto essencial da telemetria detalhada sem ampliar os campos coletados, registra inicialização saudável limitada por dia/versão e permite correlacionar opcionalmente uma operação por UUID efêmero. Todas as stacks passam por sanitização. As migrations `0008`–`0014` (incluindo alertas, 2FA e correlação v9) e o código consumidor estão preparados, mas ainda não foram implantados; devem ser aplicados juntos no próximo deploy controlado do Worker.
- Telemetria anônima registra saúde/falhas localmente; Sentry só opera após consentimento e com **Relatórios opcionais** ativos. Falhas remotas nunca quebram a otimização.
- Dashboard administrativo possui filtros, visão de telemetria e bugs, feeds compactos expansíveis e tratamento defensivo de falhas de rede/respostas inválidas. A sessão administrativa é opaca, revogável e válida por 30 dias; não persiste senha ou token no navegador.
- O dashboard é um centro de comando: preserva avisos ao vivo com severidade e mostra sinais agregados de inicializações saudáveis, fluxos abandonados e resultados/duração do benchmark oficial, sem permitir contar pessoas ou reconstruir sessões.
- Cookies administrativos cross-site usam `SameSite=None`; toda mutação `POST /admin/*` exige a origem exata do dashboard, e o dashboard publica CSP restritiva/anti-frame. O Worker não expõe mais a rota legada `workers.dev`; `api.vemryx.com` é a origem pública.
- Cobrança recorrente (Asaas) no Worker/D1: `billing_checkout_intents`, `billing_webhook_events` (idempotente por `provider_request_id`), `billing_subscriptions` e `billing_payments` sustentam checkout hospedado, reconciliação e cancelamento; `account_entitlements` é o snapshot fail-closed lido por `GET /account/entitlements`. O preço vem do servidor (nunca do cliente); todo pagamento é revalidado contra a API canônica da Asaas antes de conceder Pro (nenhum campo do webhook é confiado isoladamente). A reconciliação manual é limitada a pagamentos recentes, TTL curto e uma auditoria histórica rotativa; webhooks continuam a fonte primária. Credenciais (`ASAAS_ACCESS_TOKEN`, `ASAAS_WEBHOOK_TOKEN`) e o domínio `api.vemryx.com` já estão configurados em produção; vendas permanecem desativadas até homologação (ver `docs/billing.md`, `docs/asaas-setup.md`).
- Rota `POST /ai/message` (Ralven AI) exige entitlement Pro + `ralven_ai` validado no servidor, e-mail verificado, rate limit por Firebase UID, UUID idempotente e reserva de orçamento em D1 antes de chamar o provedor OpenAI; a credencial (`OPENAI_API_KEY`) só existe no Worker. A resposta é estruturada (texto + um perfil padrão opcional), limitada e sem ferramentas nem execução direta; o ledger não grava conteúdo da conversa. A migration `0011_ralven_ai_foundation.sql` é aditiva e ainda requer deploy controlado junto da ativação. Ver `docs/ralven-ai.md`.
- Diagnósticos do updater usam catálogo fechado de códigos sem texto livre, distinguem Development/Production e seguem legíveis para clientes anteriores; o dashboard exibe o diagnóstico permitido, não stack traces ou paths.

### Atualização e distribuição

- Cadeia de atualização é independente/transacional, com staging, validações de origem/integridade, estado durável, health receipt, recuperação/rollback e proteção contra downgrade conforme documentação específica.
- Launcher/Updater tratam locks, corridas e timeouts; a supervisão do lifecycle é serializada por instalação e todos os caminhos mutáveis de update recusam reparse points. Hashing/extração/verificação roda fora da UI com cancellation, comparação de hash em tempo constante e recuperação de journals órfãos.
- Instalador Inno Setup 7 é self-contained `win-x64`, usa setup x64 e mantém tarefas como atalho e startup configuráveis no modo interativo. A experiência moderna acompanha o tema do sistema, mostra progresso real e detalhes técnicos opt-in, preserva documentos RTF e mensagens customizadas em inglês, português do Brasil, espanhol e francês.
- Não existem aliases de executável, instalador ou atualização para gerações sem suporte. O importador inicial conserva apenas dados pessoais compatíveis, é unidirecional, allowlisted e protegido contra reparse points; ver `RALVEN_MIGRATION.md`.
- Pipeline de endurecimento por ofuscação da release (`scripts/Invoke-Obfuscation.ps1`, config em `build/obfuscation/Ralven.Obfuscar.xml`): ofusca Core/Windows embutidos no bundle single-file do Launcher; `scripts/Test-HardenedRuntime.ps1`/`scripts/Test-NoUnobfuscatedAssemblies.ps1` validam determinismo e ausência de assemblies não ofuscados; gate fail-closed integrado a `scripts/Build-Portable.ps1`/`Build-Installer.ps1` e ao workflow de release. Ver `docs/release-hardening.md`.
- A release pública é reconstruída a partir de fonte limpa no ambiente protegido `release-signing`; somente o runtime endurecido/ofuscado e validado avança à assinatura. Mapas de ofuscação são cifrados e guardados no caminho privado de R2, nunca anexados à release pública.
- A CI escolhe escopo por alteração e cobre política do repositório, localização, build/testes .NET, Worker, dashboard, site, SBOM e build/smoke do instalador; o gate obrigatório aceita somente todos os jobs selecionados aprovados.
- Site público, README, instalador, manifesto/checksums e release devem permanecer coerentes com a versão realmente publicada.

## 5. Pendências e decisões abertas

Somente itens ainda relevantes devem permanecer aqui. Quando resolvidos e integrados, remova-os em vez de criar uma cronologia.

1. **Ideia futura — reaplicar tweaks durante a sessão FiveM/GTA** (backlog de funcionalidade, não decisão bloqueada): o monitor local de sessão (§4) só observa presença/ausência; ajustes que precisariam ser aplicados/restaurados durante o ciclo de vida do jogo (prioridade, afinidade, core parking, timer resolution e semelhantes) continuam fora do catálogo até existir arquitetura segura de reversão mesmo se o Ralven for encerrado. Ver `docs/graphics-optimizations-backlog.md`.
2. **GTAV Enhanced** — sem suporte operacional; requer adaptador/projeto específico antes de habilitar qualquer ação.
3. **Authenticode público** — executáveis e instalador ainda não possuem assinatura de publisher confiável; a implementação depende de certificado/conta externa e deve assinar antes dos hashes e manifestos finais.
4. **Próximas majors do frontend** — TypeScript 7 ainda excede o peer range suportado pelo `typescript-eslint` vigente, e ESLint 10 ainda não é aceito por plugins do stack Next. O estado suportado permanece TypeScript 6 e ESLint 9 até os peers oficiais convergirem.
5. **Vulnerabilidades do Dependabot** — zeradas; avaliar novas atualizações pelo CI e pelo limite de compatibilidade do item 4.
6. **Campos de bug-report v5 não enviados** — `reproducibility`, `severity` e `gtaEdition` foram cogitados para o relato de bug (`BugReportWindow`) junto da telemetria v5, mas ficaram fora da integração: o Worker não tem schema/validação para eles em `bug_reports`, e a UI não os preenche hoje. Requer trabalho conjunto de UI + backend antes de existir.

## 6. Baseline de validação registrada

Estes números são **referência do último estado validado**, não substituem testes da branch atual.

- **10/09/2026 — `dev/proxima-versao` integrada:** build Release sem avisos e **1.570 testes .NET** aprovados. A CI completa do PR #177 aprovou política/escopo, .NET, Worker, dashboard, site, SBOM, build/smoke do instalador e gate obrigatório; a validação local também gerou o instalador e percorreu instalar → atualizar → validar 748 arquivos → desinstalar preservando dados. A versão pública permanece `v1.6.1`; publicação continua dependente do fluxo oficial.

## 7. Comandos essenciais

Na raiz:

```powershell
dotnet restore Ralven.slnx
dotnet build Ralven.slnx --configuration Release --no-restore
dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1
dotnet format Ralven.slnx --verify-no-changes
.\scripts\Verify-Safety.ps1
git diff --check
.\scripts\Start-DevelopmentApp.ps1
```

Worker:

```powershell
Set-Location infra\cloudflare-worker
npm test
npm audit
```

Dashboard/site: execute testes, lint, typecheck e build definidos nos respectivos `package.json` quando a superfície for alterada.

Build/distribuição, quando aplicável:

```powershell
.\scripts\Build-Portable.ps1
.\scripts\Build-Installer.ps1 -Version <versão>
```

## 8. Release e operações remotas

- `main` não recebe desenvolvimento normal. Integração ocorre em `dev/proxima-versao`; publicação oficial segue `AI_RULES.md`.
- Não inferir autorização de push/deploy/release a partir de um commit local ou de uma validação bem-sucedida.
- Antes de calcular versão ou publicar, confirme tags/releases reais e o diff desde a última tag pública confirmada neste snapshot.
- Deploy do Worker, Pages, release, tags, assets e demais operações remotas devem seguir as permissões e gatilhos definidos em `AI_RULES.md`.
- Release pública exige coerência entre código, versão, `CHANGELOG.md`, GitHub Release, instalador, updater, site e artefatos.

## 9. Referências por domínio

Use `AI_RULES.md` para Git/integração/release; `docs/safety.md` e `docs/architecture.md` para invariantes; `docs/telemetry.md`, `docs/installer.md`, `docs/graphics-optimizations-backlog.md` e `infra/cloudflare-worker/README.md` para seus domínios. `PROJECT_HISTORY.md` é histórico e não leitura padrão.
