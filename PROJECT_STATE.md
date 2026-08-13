# Estado atual do projeto

> Documento canônico e deliberadamente curto. Ele descreve **o estado vigente**, não o histórico de implementação.
> Código, testes, Git e documentação especializada prevalecem se houver divergência. Para histórico detalhado, consulte `PROJECT_HISTORY.md` somente quando a tarefa realmente exigir contexto antigo.

## 1. Snapshot

- **Produto:** FiveMCleaner, aplicativo desktop Windows para otimização transparente, reversível e orientada por diagnóstico do FiveM para **GTAV Legacy**.
- **Integração:** `dev/proxima-versao` é a branch de integração da próxima versão; `main` representa a linha pública/estável. O fluxo de branches, worktrees, Pull Requests, integração e release é definido em `AI_RULES.md`.
- **Último estado consolidado neste documento-fonte:** 13/08/2026. Antes de qualquer trabalho, confirme o estado real com Git e os testes atuais.
- **Versão pública:** `v1.3.2`, publicada em 07/08/2026 a partir de `main`. Confirme tags/releases antes de iniciar uma nova publicação.
- **Atalho de desenvolvimento:** `FiveMCleaner - Desenvolvimento` deve representar somente o estado integrado de `dev/proxima-versao` e usar `scripts\Start-DevelopmentApp.ps1`. A reconstrução/validação do atalho pertence ao fluxo de integração, não a tarefas paralelas isoladas.

## 2. Objetivo e invariantes de segurança

- Priorizar mudanças pequenas, verificáveis, diagnosticáveis e reversíveis; nunca prometer ganho universal de FPS.
- Suporte operacional somente a **FiveM para GTAV Legacy**. GTAV Enhanced deve ser detectado/bloqueado com segurança até existir suporte específico.
- Nunca desativar Defender, Firewall, SmartScreen, UAC, Windows Update ou serviços essenciais; nunca criar exclusões de antivírus.
- Nunca injetar código, alterar memória de processos, instalar driver de kernel, usar hook gráfico ou baixar/executar código arbitrário como mecanismo de otimização.
- Caches e arquivos sensíveis são tratados por allowlist. Autenticação, `game-storage`, NUI storage, configurações e plugins não são lixo automático.
- Perfis **Leve, Médio e Agressivo** são composições versionadas de ações. O usuário escolhe o perfil, não uma lista arbitrária de tweaks.
- Cada ação deve ter escopo conhecido, pré-condições, validação, resultado tipado e rollback quando aplicável.
- O fluxo padrão é isolado por ação: verificar → aplicar → validar → registrar. Falha normal reverte somente a ação afetada; falha crítica pode abortar o restante. O broker elevado mantém contrato estrito e allowlisted.
- Não medir FPS ao vivo dentro do FiveM por overlay/hook. O benchmark implementado é o benchmark **standalone oficial do GTA V**, opt-in e fora de uma sessão FiveM.
- Dados indisponíveis por limitações do Windows/driver devem aparecer como indisponíveis; nunca estimar ou inventar métricas.

Documentos normativos: `docs/safety.md` e `docs/architecture.md`.

## 3. Arquitetura atual

### Solução .NET

`FiveMCleaner.slnx` separa responsabilidades. A árvore de `src/` possui nove projetos principais:

- `FiveMCleaner.App` — WPF, navegação, localização, tema, conta, apresentação, progresso e interação.
- `FiveMCleaner.Contracts` — DTOs, IDs, enums e contratos compartilhados; os estados persistidos de transação e journal são contratos duráveis append-only.
- `FiveMCleaner.Core` — catálogo de ações, perfis, planejamento e regras independentes de Windows/UI; o planejamento é puro e recebe explicitamente suas entradas variáveis.
- `FiveMCleaner.Windows` — descoberta e adaptadores Windows, filesystem, registro, diagnósticos e ações permitidas.
- `FiveMCleaner.Broker` — processo administrativo efêmero e allowlisted; sem shell/comandos arbitrários.
- `FiveMCleaner.Launcher` — inicialização/ativação do runtime e coordenação do fluxo de atualização.
- `FiveMCleaner.Updater` — atualização independente e staging/aplicação da atualização.
- `FiveMCleaner.UpdateRuntime` — contratos/estado durável usados pela cadeia de atualização e recuperação.
- `FiveMCleaner.ReleaseTool` — suporte à preparação/validação de artefatos de release.

Testes .NET ficam em `tests/FiveMCleaner.Tests/`.

A toolchain integrada usa .NET 10 LTS com SDK 10.0.303, C# 14 fixo e NuGet Central Package Management em `Directory.Packages.props`. Os testes usam xUnit v3 sobre Microsoft Testing Platform, com cobertura via `coverlet.MTP`.

### Infraestrutura e web

- `infra/cloudflare-worker/` — backend Cloudflare Worker + D1 para telemetria, relatos de bug e perfil de conta.
- `infra/dashboard/` — painel administrativo privado da telemetria/bugs.
- `website/` — fonte única do site/landing page, gerada como export estático nativo do Next.js para GitHub Pages.
- `installer/` — Inno Setup 7 em arquitetura x64.
- `scripts/` — build, validação, release, smoke tests e launcher de desenvolvimento.
- `.github/workflows/` — CI de .NET/site/Worker/dashboard, Pages, SBOM e release. Dependabot cobre NuGet, npm e Actions; o CodeQL usa o default setup do GitHub para C#, JavaScript/TypeScript e Actions.

Node 24.19 LTS é o baseline versionado para site, Worker e dashboard.

### Persistência local

Preferências, journals, solicitações efêmeras, filas e logs locais ficam sob `%LOCALAPPDATA%\FiveMCleaner`; não gravar dados mutáveis na pasta de instalação.

## 4. Estado funcional relevante

### Interface

- Aplicação WPF com WPF-UI/Fluent, Mica, tema claro/escuro/sistema e localização.
- Janela principal inicia/restaura maximizada e preserva comportamento de bandeja.
- Visão geral apresenta diagnóstico/prontidão e monitoramento local de recursos; coleta pausa quando a superfície não está ativa.
- Aba **Otimizador** foi reconstruída em 07/08: trilha Preparar → Executar → Resultado, cena `OptimizerCore3D`, seleção Leve/Médio/Agressivo, resumo do computador, execução/progresso e resultado redesenhados.
- Animações novas do Otimizador evitam `ScaleTransform` em elementos interativos, seguindo a regra já adotada para impedir deslocamento de listas no hover.
- Smoke de captura aceita seleção de página via `--capture-page=Optimizer|History|Settings|Dashboard`.

### Motor de otimização e diagnóstico

- `ActionCatalog.CurrentVersion` mais recente registrado: **14**.
- Diagnósticos cobrem FiveM/GTA, CPU, GPU, RAM, armazenamento, cache, processos, rede, pagefile/commit, drivers, monitor, HAGS, energia, WHEA, sinais de throttling e outros dados obtidos por APIs nativas/best-effort.
- Existem diagnósticos somente leitura para gargalo provável, overlays/captura, logs do FiveM e orientação de medição pelas ferramentas oficiais do FiveM.
- Relatório estruturado e relatório técnico sanitizado podem ser copiados/salvos explicitamente pelo usuário.
- Journal, snapshots e rollback preservam rastreabilidade das ações; a revalidação de planos compara integralmente os metadados de ações e usa a reconstrução canônica da requisição.
- Ações XML de gráficos usam uma transação segura compartilhada; inspeção de processos e adaptadores de GPU têm primitivas de leitura separadas das mutações.
- Diagnóstico de criadores reconhece OBS, Streamlabs Desktop e TikTok LIVE Studio sem fechar processos nem inferir que uma live está ativa.

### Conta e autenticação

- Autenticação do aplicativo usa **Firebase Authentication REST** para cadastro, login, verificação de e-mail, recuperação, reautenticação, alteração de e-mail/senha e exclusão de conta.
- O ID Token fica em memória; refresh token opcional é persistido protegido por DPAPI. O **Firebase UID** é o identificador interno permanente, nunca o e-mail.
- Perfil complementar (nome, sobrenome e username único) é armazenado no Worker/D1, indexado pelo UID autenticado.
- Worker valida ID Token Firebase por RS256/JWKS, incluindo `aud`, `iss`, expiração e `sub`.
- Login com Google usa OAuth2 + PKCE com redirect loopback e foi testado ponta a ponta com credenciais reais de desenvolvimento.
- Segredos/configuração local de Google não são versionados; overlays `Config/appsettings.{Development,Production}.local.json` são git-ignorados.
- Gerenciamento de conta fica em Configurações. Avatar é normalizado e armazenado **somente localmente** por enquanto; não existe backend de avatar.

### Telemetria, bugs e backend

- FormSubmit foi removido do código de desenvolvimento. O transporte atual usa Cloudflare Worker/D1.
- Infraestrutura registrada como ativa: `/telemetry`, `POST /bugs` e `GET /api/bugs`; relatos de bug são texto, e-mail opcional e trecho de log opcional. **Não há anexo/R2**.
- Telemetria e crash reporting obedecem consentimento e allowlists; falhas de envio nunca devem bloquear ou alterar o resultado da otimização.
- Sentry é usado para crash reporting do aplicativo, com sanitização/configuração centralizada e sem transformar o SDK em dependência das camadas Core/Windows/Broker.
- Dashboard administrativo possui filtros, visão de telemetria e bugs e tratamento defensivo de falhas de rede/respostas inválidas.

### Atualização e distribuição

- Cadeia de atualização é independente/transacional, com staging, validações de origem/integridade, estado durável, health receipt, recuperação/rollback e proteção contra downgrade conforme documentação específica.
- Launcher/Updater tratam locks transitórios e corridas de processo; broker e fluxos elevados possuem timeouts para evitar bloqueio indefinido.
- Instalador Inno Setup 7 é self-contained `win-x64`, usa setup x64 e mantém tarefas como atalho e startup configuráveis no modo interativo.
- Site público, README, instalador, manifesto/checksums e release devem permanecer coerentes com a versão realmente publicada.

## 5. Pendências e decisões abertas

Somente itens ainda relevantes devem permanecer aqui. Quando resolvidos e integrados, remova-os em vez de criar uma cronologia.

1. **Deploy do Worker após mudanças de conta de 06/08** — a rota `GET /account/username-available` e seu rate limit foram implementados/testados, mas o último registro informa que `wrangler deploy` dessa revisão ainda dependia de autorização remota.
2. **Migração de contas legadas** — a migração para Firebase removeu o fluxo antigo de contas do Worker. Se existirem usuários/dados reais do sistema legado que precisem ser preservados, definir migração ou recriação/redefinição antes de uma release que dependa disso.
3. **Avatar remoto** — avatar permanece local; backend/armazenamento remoto não foi implementado.
4. **Validação real do Otimizador redesenhado** — estados de execução e resultado do redesign de 07/08 foram verificados visualmente com `--demo-synthetic`; uma execução real completa ainda é a prova final registrada para esses estados.
5. **Watcher de sessão FiveM/GTA** — ajustes que precisariam ser aplicados/restaurados durante o ciclo de vida do jogo (prioridade, afinidade, core parking, timer resolution e semelhantes) continuam fora do catálogo até existir uma arquitetura segura de monitoramento e reversão mesmo se o FiveMCleaner for encerrado. Ver `docs/graphics-optimizations-backlog.md`.
6. **GTAV Enhanced** — sem suporte operacional; requer adaptador/projeto específico antes de habilitar qualquer ação.
7. **Branding opcional do repositório** — social preview/banner não foi definido por depender de decisão de marca; não é bloqueador técnico.
8. **Authenticode público** — executáveis e instalador ainda não possuem assinatura de publisher confiável; a implementação depende de certificado/conta externa e deve assinar antes dos hashes e manifestos finais.
9. **Próximas majors do frontend** — TypeScript 7 ainda excede o peer range suportado pelo `typescript-eslint` vigente, e ESLint 10 ainda não é aceito por plugins do stack Next. O estado suportado permanece TypeScript 6 e ESLint 9 até os peers oficiais convergirem.

## 6. Baseline de validação registrada

Estes números são **referência histórica do último estado validado**, não substituem testes da branch atual.

- **13/08/2026:** após integrar a refatoração de contratos/Core, infraestrutura Windows, ações XML, App e IPC, `dotnet build` Release sem restore, suíte .NET Release, `dotnet format --verify-no-changes`, `scripts/Verify-Safety.ps1` e `git diff --check` foram aprovados. Site: lint, typecheck, export estático e **3 testes** aprovados. Os gates completos de Worker, dashboard, SBOM e CI do GitHub também passaram nos PRs integrados.

Ao alterar uma superfície, execute a validação aplicável novamente e use os resultados atuais no PR. Nunca use estes números para afirmar que código posterior foi testado.

## 7. Comandos essenciais

Na raiz:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
dotnet format FiveMCleaner.slnx --verify-no-changes
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
- Antes de calcular versão ou publicar, confirme tags/releases reais e o diff desde `v1.3.2`, a versão pública confirmada neste snapshot.
- Deploy do Worker, Pages, release, tags, assets e demais operações remotas devem seguir as permissões e gatilhos definidos em `AI_RULES.md`.
- Release pública exige coerência entre código, versão, `CHANGELOG.md`, GitHub Release, instalador, updater, site e artefatos.

## 9. Documentação a consultar por domínio

Leia somente quando a tarefa tocar o domínio correspondente:

- `AI_RULES.md` — governança obrigatória de agentes, Git, PRs, integração e release.
- `docs/safety.md` — limites de segurança e operações proibidas.
- `docs/architecture.md` — fronteiras e contratos arquiteturais.
- `docs/telemetry.md` — contrato de telemetria/privacidade.
- `docs/installer.md` — instalador e release.
- `docs/graphics-optimizations-backlog.md` — decisões e backlog técnico de otimizações.
- `infra/cloudflare-worker/README.md` — operação/configuração do Worker e conta.
- `PROJECT_HISTORY.md` — histórico detalhado; **não é leitura padrão**.

## 10. Regra de manutenção deste arquivo

`PROJECT_STATE.md` deve continuar pequeno. Ele não é changelog, diário de agente, relatório de PR nem arquivo de auditoria.

- Atualizar preferencialmente **após integração**, refletindo somente o estado consolidado.
- Não registrar nomes de agentes, branches temporárias, hashes de commits ou uma seção por tarefa concluída.
- Não duplicar documentação especializada; apontar para o documento canônico.
- Manter apenas: arquitetura vigente, invariantes, capacidades atuais, decisões abertas, pendências reais e último baseline de validação.
- Ao resolver uma pendência, removê-la ou substituir o estado correspondente; não preservar a história da resolução aqui.
- Se uma informação for apenas histórica, movê-la para `PROJECT_HISTORY.md` ou deixá-la no Git/PR correspondente.
- **Meta operacional:** aproximadamente 200 linhas e preferencialmente menos de 20 KB. Se ultrapassar isso de forma sustentada, compactar novamente antes de adicionar novas seções.
