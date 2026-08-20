# FiveMCleaner — Claude Code

Instruções persistentes específicas deste repositório. Mantenha este arquivo curto, estável e focado no que Claude não deve inferir sozinho.
## Missão e prioridades

FiveMCleaner é um aplicativo Windows de diagnóstico, limpeza e otimização segura para FiveM sobre GTAV Legacy.

Priorize, nesta ordem: segurança e dados do usuário; reversibilidade; correção/confiabilidade; transparência; UX; desempenho; conveniência de implementação.

Nunca troque segurança, reversibilidade ou correção por uma otimização mais agressiva ou por menos código.
## Contexto obrigatório

Antes de qualquer tarefa que possa alterar o repositório:

1. localize a raiz Git e inspecione `git status`, branch, worktrees e histórico recente;
2. leia `AI_RULES.md`;
3. leia `PROJECT_STATE.md` como snapshot do estado integrado;
4. leia somente a documentação relacionada à área afetada;
5. inspecione código e testes antes de editar.

Leia `docs/architecture.md` para limites arquiteturais; `docs/safety.md` para ações de sistema, privilégios e rollback; `docs/telemetry.md` para telemetria; `docs/bug-reports.md` para privacidade; `docs/release-preview.md` para updater/release; `docs/research.md` para fundamentação técnica.

`PROJECT_HISTORY.md` não é contexto padrão. Consulte somente quando houver necessidade histórica concreta. Não carregue documentação, PRs antigos ou arquivos sem relação com a tarefa apenas para “ter contexto”.
## Fonte da verdade

Em caso de divergência, priorize:

1. código e testes vigentes;
2. governança de `AI_RULES.md`;
3. estado atual de `PROJECT_STATE.md`;
4. documentação especializada atual;
5. Git recente;
6. histórico.

Não preserve comportamento obsoleto só porque aparece em documentação antiga.
## Forma de trabalhar

- Investigue antes de editar e implemente quando o pedido for de implementação.
- Resolva autonomamente detalhes mecânicos inferíveis com segurança.
- Peça decisão ao usuário somente para escolhas reais de produto, segurança, compatibilidade ou comportamento.
- Faça mudanças coesas e limitadas ao escopo; sem limpeza, reformatação ou refatoração oportunista.
- Reutilize abstrações e padrões existentes antes de criar outra camada.
- Não adicione dependência sem necessidade clara.
- Não invente requisitos, métricas, APIs, resultados ou comportamento.
- Verifique fatos externos relevantes antes de codificar.
- Paralelize leituras independentes; evite escritas concorrentes no mesmo estado.
## Git e concorrência

O fluxo completo e as autorizações estão em `AI_RULES.md`. Essenciais:

- `main` contém somente versões públicas.
- `dev/proxima-versao` é a branch oficial de integração.
- Tarefas normais usam branch temporária baseada na `dev/proxima-versao`, preferencialmente em worktree exclusivo.
- Nunca troque a branch de checkout possivelmente usado por outro agente.
- Preserve alterações de terceiros; nunca use `git reset --hard`, force push ou descarte trabalho alheio.
- Commits seguem Conventional Commits.
- Quando permitido e disponível, tarefa concluída termina em PR para `dev/proxima-versao`.
- Não faça merge do próprio PR apenas por ter terminado.
- Não altere `main`, tags, releases, deploys ou artefatos públicos sem o gatilho explícito definido em `AI_RULES.md`.
- Resolva conflitos semanticamente; ausência de conflito textual não prova compatibilidade lógica.
## Arquitetura

Respeite as responsabilidades existentes:

- `FiveMCleaner.App`: WPF, composição e apresentação sem elevação permanente.
- `FiveMCleaner.Contracts`: contratos compartilhados.
- `FiveMCleaner.Core`: políticas, orquestração, execução e rollback.
- `FiveMCleaner.Windows`: integrações Windows/FiveM.
- `FiveMCleaner.Broker`: operações administrativas tipadas e de privilégio mínimo.
- `FiveMCleaner.Launcher`: inicialização e health-check pós-update.
- `FiveMCleaner.Updater`: staging, ativação e rollback de updates.
- `FiveMCleaner.UpdateRuntime`: primitivas seguras do updater.
- `FiveMCleaner.ReleaseTool`: empacotamento e assinatura.
- `infra/cloudflare-worker`, `infra/dashboard`, `website` e `installer`: superfícies remotas/distribuição.
- `tests/FiveMCleaner.Tests`: testes .NET do produto.

Não crie dependência circular nem mova responsabilidades entre camadas por conveniência local. Prefira contratos pequenos e explícitos entre processos e camadas.
## Invariantes de segurança

O comportamento seguro existente é parte do produto:

- FiveM/GTAV Legacy é o alvo suportado; não aplique otimizações ao GTAV Enhanced sem suporte validado.
- Diagnostique antes de alterar e preserve prévia das ações relevantes.
- Ações mutáveis devem respeitar snapshot, journal, validação e rollback quando aplicável.
- Use privilégio mínimo.
- O broker elevado aceita apenas operações tipadas/allowlisted, nunca shell arbitrário vindo da UI.
- Não desative Defender, firewall, Windows Update ou serviços essenciais como “otimização”.
- Não crie exclusões de antivírus para contornar detecção.
- Não injete código nem modifique binários do jogo.
- Não use prioridade `Realtime`, afinidade fixa ou desativação de SMT/Hyper-Threading como tweak.
- Não apague `game-storage`, credenciais, configurações, plugins ou dados protegidos.
- Limpeza ampla de cache não é ganho garantido de FPS.
- Não prometa FPS, latência ou ganho de desempenho sem evidência reproduzível.
- Rollback deve preservar mudanças posteriores feitas pelo usuário.

Nova ação de sistema precisa ter risco, pré-condições, efeito, privilégio, validação e rollback claramente definidos.
## C# / .NET

O repositório usa .NET 10, nullable habilitado, implicit usings e C# `latest`.

- Respeite `.editorconfig`: 4 espaços em C#/XAML/MSBuild; 2 em Markdown/JSON/YAML; CRLF e newline final.
- Investigue warnings novos; não os esconda.
- Evite `async void`, exceto handlers apropriados.
- Não use fire-and-forget sem supervisão de falha e ciclo de vida.
- Propague `CancellationToken` quando o fluxo suportar cancelamento.
- Não bloqueie a UI com I/O ou trabalho pesado.
- Trate falhas de I/O, rede, processo e privilégio no limite apropriado; não engula exceções.
- Prefira tipos/estados explícitos a strings mágicas.
- Preserve compatibilidade de dados persistidos e contratos externos salvo migração intencional.
## WPF, UX e localização

- Siga padrões visuais existentes antes de criar sistema paralelo.
- Preserve DPI, teclado, foco, acessibilidade e contraste.
- Animação não deve prejudicar desempenho, layout ou previsibilidade.
- Não altere comportamento funcional apenas para facilitar composição visual.
- Texto visível que participa da localização deve usar o mecanismo de recursos existente; não introduza literais hardcoded em telas localizadas.
- Ao adicionar chave, mantenha os idiomas suportados coerentes.
- Não coloque lógica de negócio nova no code-behind quando ela pertence ao view model ou serviço existente.
- Para revisão visual, prefira os modos de demonstração/captura já existentes quando suficientes.
## Segurança remota e privacidade

- Nunca versione segredo, token, senha, client secret, credencial administrativa ou dado local sensível.
- Use os mecanismos locais git-ignored/secrets existentes.
- Trate entrada de rede como não confiável e valide/autentique no servidor.
- Não registre tokens, senhas, conteúdo privado ou PII desnecessária.
- Minimize coleta e retenção de dados.
- Mudanças de telemetria, consentimento ou autenticação exigem atenção especial à compatibilidade e privacidade.
- Não faça deploy remoto apenas porque o código local ficou pronto; siga `AI_RULES.md`.
## Pesquisa e otimizações

Para Windows, FiveM, GTAV, drivers, APIs e tweaks:

- prefira documentação oficial, código-fonte primário e fabricantes;
- diferencie fato documentado, comportamento observado e inferência;
- não transforme tweak popular de fórum em otimização oficial sem evidência;
- prefira mudança mensurável, conservadora e reversível a alteração global opaca.
## Validação

Durante o desenvolvimento, rode primeiro os testes mais próximos da mudança. Para validação .NET completa, quando aplicável:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
dotnet format FiveMCleaner.slnx --verify-no-changes
.\scripts\Verify-Safety.ps1
git diff --check
```

Não repita `restore` sem necessidade. Para Worker, dashboard ou site, use os scripts versionados no respectivo `package.json`; não invente comandos.

Nunca diga que build, teste, lint, smoke test, deploy ou fluxo manual passou sem ter executado e observado o resultado. Se teste existente falhar, determine a causa, corrija regressões introduzidas pela tarefa e nunca enfraqueça o teste só para obter verde.
## Desenvolvimento sem FiveM local

Build, testes unitários, UI em modo demo e desenvolvimento comum não devem depender artificialmente de uma instalação do FiveM.

Quando o comportamento real exigir FiveM/GTAV Legacy, preserve a exigência em produção e use abstrações, fixtures ou modos de demonstração para testes que não precisam da integração real.

Nunca descreva uma simulação como validação real com FiveM instalado.
## Estado, versão e release

- Tarefas isoladas não editam `PROJECT_STATE.md` ou `PROJECT_HISTORY.md` por padrão.
- O integrador mantém `PROJECT_STATE.md` como snapshot compacto, não changelog.
- Não altere número de versão em tarefa normal.
- `CHANGELOG.md`, versão, tag, release, instalador, site e Release Notes são coordenados apenas no fluxo oficial de publicação.
- Release Notes refletem somente mudanças realmente integradas e seguem `AI_RULES.md`.
## Conclusão

Antes de encerrar:

- revise o diff inteiro e remova mudanças acidentais/temporárias;
- confirme que nenhum segredo ou dado local entrou no diff;
- execute validações proporcionais ao risco;
- adicione testes para regressão ou comportamento novo relevante;
- verifique compatibilidade lógica com trabalho concorrente quando aplicável;
- faça commits profissionais e prepare push/PR conforme `AI_RULES.md`;
- informe objetivamente resultado, validações, branch/PR e limitações reais.

Não esconda falhas nem invente problemas para parecer cauteloso.
## Comunicação

Seja direto e orientado à execução. Em tarefas longas, dê atualizações curtas somente com progresso relevante, descoberta importante ou bloqueio real.

No resultado final, priorize: resultado; validações; branch/PR; riscos ou pendências reais. Não narre cada comando nem despeje raciocínio interno.
