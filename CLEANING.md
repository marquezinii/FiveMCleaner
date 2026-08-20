# Plano de Refatoração Profunda e Remoção de Dead Code — FiveMCleaner

> **Status deste plano:** a Fase 0 já foi enviada para execução antes desta revisão. **Não reinicie nem reexecute a Fase 0 automaticamente.** As Fases 1–10 abaixo substituem o roteiro antigo e devem partir do estado real produzido pela Fase 0 depois que ele estiver validado e integrado.

## 1. Objetivo

Este documento define uma refatoração ampla, conservadora e verificável do FiveMCleaner, com quatro objetivos principais:

1. remover dead code, artefatos órfãos e dependências sem uso comprovado;
2. reduzir duplicação, acoplamento desnecessário e responsabilidades misturadas;
3. consolidar as fronteiras arquiteturais já existentes sem reescrever o produto por estética;
4. deixar o repositório mais simples de entender, testar, evoluir e auditar sem enfraquecer segurança, reversibilidade ou comportamento atual.

Esta **não é uma fase de novas features**, redesign de produto ou expansão de escopo. Se uma oportunidade de feature aparecer durante a limpeza, registre-a como dívida/backlog e continue a refatoração.

---

## 2. Fonte da verdade e contexto atual

A ordem de confiança durante toda a execução é:

1. código e testes vigentes no worktree da tarefa;
2. `AI_RULES.md`;
3. `CLAUDE.md` / `AGENTS.md`, quando aplicável ao agente;
4. `PROJECT_STATE.md`;
5. documentação especializada (`docs/architecture.md`, `docs/safety.md`, etc.);
6. histórico Git e relatórios antigos em `.ai/tasks/`.

O plano deve se adaptar ao estado real do código. **Nunca restaure uma implementação antiga apenas porque este documento a menciona.**

### Estrutura que precisa entrar no escopo

A solução atual não é somente App/Core/Windows/Broker. O sweep completo deve considerar:

- `src/FiveMCleaner.App`
- `src/FiveMCleaner.Contracts`
- `src/FiveMCleaner.Core`
- `src/FiveMCleaner.Windows`
- `src/FiveMCleaner.Broker`
- `src/FiveMCleaner.Launcher`
- `src/FiveMCleaner.Updater`
- `src/FiveMCleaner.UpdateRuntime`
- `src/FiveMCleaner.ReleaseTool`
- `tests/FiveMCleaner.Tests`
- `infra/cloudflare-worker`
- `infra/dashboard`
- `website`
- `installer`
- `scripts`
- `.github/workflows`
- arquivos de build/configuração da raiz
- `docs`
- recursos, assets, localização e configuração associados às superfícies acima.

### Trabalho recente que NÃO deve ser repetido cegamente

O projeto já recebeu rodadas anteriores de:

- redução de overengineering/dead surfaces;
- tech-debt e deduplicação pontual;
- auditoria de falhas silenciosas/startup;
- modernização de dependências/toolchain;
- decomposição de funções grandes em áreas como `WindowsTransactionEngine`, `AppOptimizationService`, `MainViewModel`, `GitHubReleaseUpdateService`, `PlanBuilder`, `ActionCatalog`, `WindowsOptimizationRuntime` e `MainWindow`.

Portanto, o objetivo agora não é repetir `Extract Method` ou deletar novamente itens já removidos. O foco deve ser **responsabilidade, dependências, invariantes, duplicação semântica, dead code restante e simplificação estrutural real**.

---

## 3. Princípios não negociáveis

1. **Segurança > Reversibilidade > Corretude > Integridade > Transparência > Compatibilidade > UX > Desempenho > conveniência de implementação.**
2. **Detecção antes da ação:** nunca assumir estado de Windows/FiveM; detectar fatos antes de decidir ou escrever.
3. **Privilégio mínimo:** o App permanece sem elevação permanente; operações administrativas continuam estritamente tipadas e allowlisted no Broker.
4. **Rollback preservado:** alterações persistentes suportadas continuam reversíveis, e o rollback não deve sobrescrever mudanças posteriores do usuário.
5. **Comportamento preservado por padrão:** refatoração não é autorização para alterar política de produto.
6. **Sem feature creep:** não implementar novas otimizações, GTAV Enhanced, watcher de sessão, avatar remoto, migração de conta, Authenticode ou outra pendência de produto durante este plano.
7. **Sem redesign oportunista:** UI só muda quando necessário para remover superfície morta, corrigir acoplamento ou adaptar contratos refatorados.
8. **Sem dependência nova por conforto:** prefira reduzir dependências. Só introduza uma nova quando houver benefício arquitetural claro e demonstrável.
9. **Sem abstração pela abstração:** duas implementações parecidas não precisam de uma interface/base/helper comum se a semântica divergir.
10. **Sem “limpeza estética” massiva:** não reformate o repositório inteiro nem renomeie centenas de símbolos sem ganho estrutural real.
11. **Sem enfraquecer testes para obter verde:** ajuste/remova um teste somente quando o comportamento que ele cobre foi comprovadamente removido ou intencionalmente alterado.
12. **Sem versionamento/publicação:** fases de limpeza não alteram versão pública, tag, release, changelog público, deploy ou `main`, salvo autorização explícita fora deste plano.

---

## 4. O que conta como dead code

Dead code neste projeto inclui muito mais do que métodos C# sem referência direta.

### 4.1 Superfícies que devem ser auditadas

- tipos, métodos, propriedades, eventos e campos C#;
- interfaces e implementações sem consumidores reais;
- DTOs, enums, estados e códigos de erro obsoletos;
- Actions que não pertencem mais ao catálogo vigente;
- IDs de Action sem implementação, ou implementação sem catálogo;
- handlers, rotas e helpers de Worker sem consumidores/protocolo vigente;
- endpoints antigos ainda referenciados por App/dashboard/site;
- módulos JavaScript/TypeScript, imports e exports órfãos;
- pacotes NuGet/npm e `ProjectReference`s sem necessidade;
- XAML, `ResourceDictionary`, estilos, templates, converters e `x:Key`s sem consumidor;
- chaves `.resx` sem uso;
- imagens, fontes, ícones e outros assets órfãos;
- opções/config keys/environment variables sem leitor real;
- scripts PowerShell/shell e funções internas sem callers;
- etapas e jobs de workflow sem função atual;
- entradas do Inno Setup que apontam para artefatos/fluxos mortos;
- test doubles, fixtures e helpers usados apenas por testes de comportamento já removido;
- documentação operacional que descreve fluxo inexistente;
- compat shims cuja compatibilidade já não é necessária **desde que isso seja comprovado**.

### 4.2 Evidência mínima antes de remover

Nunca remova algo somente porque uma busca textual retornou zero referências.

Antes de apagar, verifique conforme o tipo de artefato:

- referências de compilação e project graph;
- reflection e descoberta dinâmica;
- DI/factories/registrations;
- serialização e nomes de propriedades persistidos;
- IDs/strings usados como protocolo;
- bindings, `DynamicResource`, `StaticResource`, commands e converters XAML;
- rotas e chamadas HTTP;
- scripts, workflows, installer e release tooling;
- CSS selectors/DOM IDs/event names;
- arquivos carregados por convenção;
- compatibilidade com dados persistidos e versões públicas ainda suportadas;
- consumidores remotos/deployados quando aplicável.

### 4.3 Classificação obrigatória

Todo candidato relevante deve cair em uma destas categorias:

- **DEAD-PROVEN:** comprovadamente sem consumidor e seguro para remoção;
- **DUPLICATE:** vivo, mas semanticamente duplicado e consolidável;
- **MISPLACED:** vivo, porém com responsabilidade na camada errada;
- **LEGACY-LIVE:** antigo, mas ainda necessário por compatibilidade;
- **SUSPICIOUS:** parece morto, mas a evidência não é suficiente;
- **LIVE:** usado e coerente; não tocar apenas por preferência estética.

`SUSPICIOUS` não é autorização para deletar. Registre e deixe para uma análise específica.

---

## 5. Protocolo obrigatório para cada fase

### Entrada da fase

Antes de editar:

1. leia `AI_RULES.md`, `CLAUDE.md` e o `PROJECT_STATE.md` vigente;
2. confirme branch, worktree, `git status` e histórico recente;
3. confirme que a fase anterior foi integrada ou que o worktree atual contém explicitamente seu estado validado;
4. leia apenas a documentação especializada necessária para a fase;
5. rode validações direcionadas suficientes para conhecer a baseline da área.

### Durante a fase

- trabalhe em lotes pequenos e semanticamente coerentes;
- após mudanças críticas, rode testes próximos antes de continuar;
- não misture correção funcional não relacionada com limpeza;
- quando encontrar bug real fora de escopo, registre-o; só corrija imediatamente se ele impedir a refatoração segura ou se for causado pela própria fase;
- preserve o comportamento observável salvo quando a remoção de deadcode naturalmente elimina uma superfície já inexistente no produto.

### Saída da fase

A fase só termina quando:

- build/testes aplicáveis passam;
- não há warning novo introduzido;
- `git diff --check` está limpo;
- formatação aplicável está válida;
- `Verify-Safety.ps1` passa quando a fase toca .NET/Windows/segurança;
- o diff final foi revisado por inteiro;
- não há segredo, build output, cache ou arquivo local acidental;
- o relatório `.ai/tasks/<fase>.md` registra mudanças, remoções, testes e itens deliberadamente mantidos;
- a branch fica pronta para integração segundo `AI_RULES.md`.

**Não inicie a próxima fase automaticamente.** Cada fase deve ser integrada/estabilizada antes da seguinte.

---

# Fases

## Fase 0 — Fundação, contratos e Core — JÁ ENVIADA

**Status:** já despachada antes desta revisão. Não reexecutar automaticamente.

### Objetivo original preservado

- limpar e consolidar contratos compartilhados;
- remover DTOs/estados realmente obsoletos;
- consolidar metadados das Actions;
- simplificar `PlanBuilder` e utilitários Core sem alterar regras de produto;
- alinhar documentação arquitetural afetada.

### Gate obrigatório antes da Fase 1

Quando a Fase 0 retornar:

- revisar o diff e o relatório produzido;
- confirmar que IDs/DTOs persistidos ou interprocesso não foram quebrados sem migração;
- executar a validação .NET aplicável;
- integrar a Fase 0 conforme `AI_RULES.md`;
- tratar o resultado integrado como nova baseline.

Se a Fase 0 tiver tomado decisões diferentes das descritas no roteiro antigo, **as fases seguintes se adaptam ao código atual; não reverta a Fase 0 apenas para combinar com este documento.**

---

## Fase 1 — Inventário arquitetural e censo de dead code do repositório inteiro

**Risco:** médio.
**Natureza:** investigação estruturada; poucas alterações de produção.

### Objetivo

Construir um mapa confiável antes da remoção pesada. O objetivo é evitar que cada fase redescubra dependências e, principalmente, evitar exclusões falsas em superfícies dinâmicas.

### Escopo

Mapear:

- dependências entre os nove projetos .NET;
- `ProjectReference`s e pacotes;
- dependências App → Core/Contracts/Windows/Broker;
- fronteira App ↔ Broker;
- fluxo Launcher → App/Updater → UpdateRuntime;
- fluxo ReleaseTool/installer/scripts/workflows;
- App ↔ Worker/Firebase/Sentry;
- dashboard ↔ Worker;
- site ↔ artefatos/releases;
- Actions do catálogo ↔ implementações ↔ UI ↔ testes;
- persistência local e formatos duráveis;
- recursos XAML/RESX/assets;
- rotas/config/env vars do Worker;
- scripts e jobs de CI/release.

### Entregável

Criar no relatório da fase uma tabela de candidatos com:

- símbolo/arquivo/recurso;
- categoria (`DEAD-PROVEN`, `DUPLICATE`, `MISPLACED`, `LEGACY-LIVE`, `SUSPICIOUS`, `LIVE`);
- consumidores encontrados;
- risco de remoção;
- fase responsável pela decisão final.

### Alterações permitidas

Pode remover apenas artefatos **DEAD-PROVEN de risco trivial**, totalmente desconectados das fases críticas. Não faça remoções em massa ainda.

### Critério de conclusão

As Fases 2–9 precisam ter um mapa de escopo claro e nenhuma área importante do repositório pode ficar “fora porque não apareceu no CLEANING antigo”.

---

## Fase 2 — Windows Infrastructure: descoberta, sensores e primitivas de sistema

**Risco:** alto.
**Escopo principal:** `src/FiveMCleaner.Windows/Infrastructure` e testes relacionados.

### Objetivo

Separar rigorosamente **observação do sistema** de **decisão/mutação**, reduzir duplicação de acesso ao Windows e eliminar adapters/inspectors realmente sem consumidor.

### Trabalho

1. Auditar todos os Inspectors, locators e readers de Windows/FiveM.
2. Garantir que Inspector seja read-only e reporte fatos/indisponibilidade, não política de produto.
3. Remover Inspector ou branch interna sem consumidor comprovado.
4. Consolidar acesso ao Registro em abstração tipada/coesa quando houver duplicação real.
5. Auditar `CommandRunner`, acesso a processos, filesystem, WMI/PerformanceCounter/native APIs e parsing compartilhado.
6. Remover strings mágicas repetidas de Registry/path/estado quando existir uma fonte tipada melhor.
7. Consolidar normalização/parsing duplicado somente quando a semântica for realmente a mesma.
8. Garantir que indisponibilidade de métrica continue sendo `unavailable`/equivalente, nunca estimativa inventada.
9. Manter descoberta de FiveM Legacy e bloqueio de GTAV Enhanced intactos.
10. Preservar desenvolvimento/testes sem instalação real do FiveM quando a integração real não for necessária.

### Não fazer

- não mover regra de perfil para Infrastructure;
- não transformar `CommandRunner` em shell genérica para o App;
- não adicionar novo tweak;
- não unificar helpers cuja diferença atual seja deliberada.

### Gate da fase

- testes de descoberta/diagnóstico passam;
- nenhuma mutação acidental foi introduzida em Inspectors;
- toda remoção de Inspector/utilitário tem evidência de ausência de consumidor.

---

## Fase 3 — Motor transacional, journal, runtime e rollback

**Risco:** CRÍTICO.
**Escopo principal:** `FiveMCleaner.Windows/Engine`, `WindowsOptimizationRuntime`, contratos diretamente envolvidos e testes transacionais.

### Objetivo

Simplificar o “cérebro” de execução sem enfraquecer atomicidade por ação, journal, cancelamento, rollback, falha isolada ou fronteira de privilégio.

### Observação importante

`WindowsTransactionEngine` e `WindowsOptimizationRuntime` já passaram por decomposição de funções. **Não repita uma rodada mecânica de Extract Method.** Audite agora o modelo, estados, dependências e invariantes.

### Invariantes a formalizar antes de editar

Documente/teste pelo menos:

- plano confirmado permanece imutável;
- ação verificada sem escrita continua distinta de ação aplicada;
- cada escrita persistente relevante tem estado anterior suficiente para rollback;
- journal nunca anuncia estado que ainda não foi duravelmente persistido quando isso comprometer recuperação;
- cancelamento ocorre em pontos seguros;
- falha de ação isolada não corrompe ações já comprometidas;
- falha/cancelamento de UAC não transforma ações normais já concluídas em falhas falsas;
- ações administrativas mantêm política estrita vigente;
- `Committed`, `CommittedWithErrors`, rollback e outcomes continuam semanticamente coerentes;
- recuperação/reexecução não duplica mutações de forma insegura;
- rollback preserva alterações posteriores feitas pelo usuário quando a política atual exige compare-and-restore.

### Trabalho

1. Auditar `WindowsTransactionEngine`, `TransactionJournal`, `OptimizationReportBuilder` e `WindowsOptimizationRuntime` como um fluxo único.
2. Remover estados, flags, logs, branches e opções comprovadamente inalcançáveis ou sem leitor.
3. Consolidar transições de estado duplicadas em uma única fonte quando possível.
4. Reduzir conversões repetidas entre estado interno, outcome e relatório.
5. Tornar persistência de journal explicitamente atômica onde necessário.
6. Eliminar decisão de produto do Runtime; ele deve executar/orquestrar contratos já decididos.
7. Eliminar dependências invertidas ou callbacks desnecessários se puder fazê-lo sem reescrever a arquitetura.
8. Verificar idempotência/recovery de operações relevantes.
9. Adicionar/fortalecer fault-injection e testes de falha parcial antes de remover caminhos defensivos.
10. Se existir primitiva duplicada equivalente a `AtomicFile`, `TransientRetry` ou helper de processo em outra camada, só consolidar se a direção de dependência continuar correta. **Não crie acoplamento ao UpdateRuntime apenas para economizar linhas.**

### Gate da fase

Exigir testes explícitos para sucesso, já-verificado, skip, falha, rollback, rollback-failed, cancelamento, falha crítica, falha parcial e fase administrativa.

---

## Fase 4 — Catálogo de Actions, implementações e regras de negócio

**Risco:** alto.
**Escopo principal:** `FiveMCleaner.Core` relacionado ao catálogo/políticas e `FiveMCleaner.Windows/Actions`.

### Objetivo

Garantir uma relação clara e mínima entre **definição de Action**, **implementação Windows**, **perfil**, **UI**, **telemetria** e **teste**, removendo Actions/branches auxiliares que não participem mais do produto.

### Trabalho

1. Construir matriz 1:1 entre IDs do catálogo e implementações executáveis.
2. Confirmar que nenhum ID está órfão e nenhuma implementação está escondida fora do catálogo sem motivo explícito.
3. Preservar IDs estáveis usados por journal, relatório, telemetria ou dados persistidos.
4. Auditar arquivos grandes por responsabilidade, sem repetir mera decomposição já feita.
5. Procurar duplicação de:
   - checks de processo FiveM/GTA;
   - privilégios;
   - preconditions/prerequisites;
   - filesystem safety;
   - Registry read/write;
   - verify/apply/rollback patterns;
   - parsing de estado atual/desejado.
6. Extrair helpers pequenos e específicos apenas quando houver identidade semântica.
7. Evitar um `ActionHelpers` genérico que vire depósito de tudo.
8. Separar claramente Action diagnóstica/read-only de Action mutável.
9. Auditar classes com nomes históricos/legacy como **candidatas**, não como deadcode presumido.
10. Remover Action substituída somente depois de provar que catálogo, perfis, UI, telemetria, journal e testes não dependem mais dela.
11. Garantir que `ActionMetadataDto`/definições atuais cubram risco, privilégio, reversibilidade, preconditions, criticality e documentação necessária.
12. Remover recursos/testes diretamente ligados a Actions eliminadas, mantendo o resto para a fase transversal.

### Gate da fase

- catálogo e runtime concordam sobre todas as Actions;
- nenhum ID de produção desapareceu sem análise de compatibilidade;
- testes de cada família alterada passam;
- nenhuma Action nova foi inventada durante a limpeza.

---

## Fase 5 — App WPF, composição, ViewModels, Services e recursos de UI

**Risco:** alto.
**Escopo principal:** `src/FiveMCleaner.App`.

### Objetivo

Remover superfície de UI/serviço órfã, reduzir acoplamento de composição e impedir que regras de negócio escapem para code-behind, sem transformar a fase em redesign visual.

### Trabalho

1. Auditar `MainWindow`, ViewModels e serviços como grafo de composição/lifetime.
2. Não refazer a decomposição mecânica já realizada; procurar responsabilidades ainda misturadas.
3. Remover botões, menus, states, properties, commands, events e bindings ligados a comportamento removido.
4. Identificar bindings quebrados/silenciosos e propriedades de VM sem consumidor.
5. Remover converters, styles, templates, ResourceDictionary entries e `.resx` comprovadamente órfãos.
6. Remover assets sem referência, considerando referências por URI/XAML/build action.
7. Auditar subscriptions/unsubscriptions, timers, CancellationToken, `IDisposable`, background tasks e fire-and-forget.
8. Reduzir criação manual duplicada de serviços/configuração quando já houver uma composição canônica.
9. Preservar isolamento de Sentry/telemetria/auth na camada App.
10. Preservar localização, acessibilidade, DPI, teclado, foco, tray e modos demo/capture existentes.
11. Mover lógica de negócio do code-behind apenas quando ela claramente pertence a ViewModel/serviço já existente ou a uma abstração pequena e justificada.
12. Não criar uma arquitetura MVVM nova paralela apenas para “ficar bonito”.

### Gate da fase

- smoke da UI e modos de demonstração/captura relevantes funcionam;
- não existem referências XAML quebradas;
- recursos removidos foram provados órfãos;
- comportamento visual só mudou onde a remoção/refatoração exigiu.

---

## Fase 6 — Broker, IPC e fronteira de privilégio

**Risco:** CRÍTICO.
**Escopo principal:** `src/FiveMCleaner.Broker`, contratos interprocesso e callers no App/Core.

### Objetivo

Reduzir o Broker ao menor executor privilegiado coerente com o produto, eliminando comandos, eventos, adapters e validações redundantes sem abrir a fronteira de confiança.

### Trabalho

1. Auditar fluxo completo App/Core → request tipado → Broker → Windows adapter → evento/resultado.
2. Garantir correspondência 1:1 entre comandos aceitos e implementação privilegiada real.
3. Remover comando/branch sem produtor ou executor comprovado.
4. Auditar `BrokerCommandLine`, `PlanRequestFileLoader`, `PlanValidator`, `NamedPipeEventWriter`, `ElevationGuard`, adapters e logs.
5. Consolidar validação duplicada apenas sem diminuir defense-in-depth.
6. Preservar allowlist e recusar payload/comando livre.
7. Auditar lifetime e limpeza de arquivos efêmeros/request files.
8. Auditar timeout, cancellation, encerramento de pipe/processo e falha de UAC.
9. Remover eventos/log fields sem consumidor quando não fizerem parte de diagnóstico suportado.
10. Garantir que o Broker não conheça navegação, UI, telemetria ou política ampla de produto.
11. Adicionar testes negativos para request inválida, ação não permitida, arquivo adulterado, estado inesperado e timeout quando a infraestrutura permitir.

### Gate da fase

Nenhuma simplificação pode ampliar o poder do processo elevado. Menos código é bom; uma superfície de privilégio mais genérica não é.

---

## Fase 7 — Launcher, Updater, UpdateRuntime, ReleaseTool e distribuição

**Risco:** CRÍTICO.
**Escopo principal:**

- `FiveMCleaner.Launcher`
- `FiveMCleaner.Updater`
- `FiveMCleaner.UpdateRuntime`
- `FiveMCleaner.ReleaseTool`
- `installer`
- scripts de build/release/update
- workflows de CI/release relacionados.

### Objetivo

Fazer a mesma limpeza profunda no subsistema que o plano antigo praticamente ignorava, preservando atualização transacional, integridade, downgrade protection, health-check e rollback.

### Trabalho

1. Mapear o state machine de update do download/staging até ativação, health receipt, recovery e rollback.
2. Auditar e consolidar primitivas equivalentes de:
   - atomic file;
   - retry transitório;
   - temporary directory;
   - process wait/lock handling;
   - hashing/signature/manifest parsing;
   - version comparison;
   - journal/recovery state.
3. Remover estados de update inalcançáveis, campos nunca lidos e compat shims sem consumidor suportado.
4. Eliminar duplicação entre Launcher/Updater/UpdateRuntime/ReleaseTool sem inverter dependências.
5. Preservar validação de origem, versão, tamanho, hash e assinatura existente.
6. Preservar ordem correta de assinatura/hash/manifesto exigida pelo pipeline.
7. Preservar version floor/downgrade protection.
8. Preservar health receipt e recuperação quando a nova versão não confirma saúde.
9. Auditar scripts de build/portable/installer e funções compartilhadas; remover scripts/funções sem caller real.
10. Auditar Inno Setup por arquivos, tasks, registry entries e flags obsoletos.
11. Auditar workflows por etapas redundantes ou duplicadas, sem reduzir os gates de segurança da release.
12. Remover package/project references do subsistema sem uso comprovado.
13. Validar empacotamento local e simulações suportadas. **Não publicar release real.**

### Gate da fase

Testes de update/recovery devem provar pelo menos sucesso, pacote inválido, hash/signature inválidos, downgrade, lock transitório, falha na ativação e rollback/health-check conforme os mecanismos existentes.

---

## Fase 8 — Worker, dashboard e website

**Risco:** alto.
**Escopo principal:** `infra/cloudflare-worker`, `infra/dashboard`, `website`.

### Objetivo

Remover deadcode e duplicação das superfícies JavaScript/TypeScript e alinhar contratos remotos com o cliente atual, sem confundir “sem caller no código atual” com “seguro para remover de um backend implantado”.

### Observação importante

Já houve tech-debt anterior removendo endpoints de stats órfãos e consolidando filtros/helpers do Worker/dashboard. **Não recrie essa mesma tarefa.** Parta do estado vigente.

### Worker

1. Inventariar rota → handler → auth → schema/query → caller/teste.
2. Remover rota/helper/field sem consumidor somente após considerar clientes publicados e compatibilidade remota.
3. Preservar Firebase UID como identidade interna das rotas autenticadas.
4. Preservar verificação Firebase, sanitização, rate limit, CORS e regras de privacidade.
5. Auditar telemetria/bug-report allowlists e campos que já não possuem produtor/consumidor.
6. Auditar env vars/config bindings e remover configuração morta.
7. Não apagar migration D1 histórica somente por não ser referenciada pelo código atual; migrations podem representar estado já aplicado.
8. Remover test helpers/fixtures apenas quando o comportamento correspondente tiver sido removido.

### Dashboard

1. Mapear chamadas reais ao Worker.
2. Remover views, renderers, filtros, helpers, CSS e assets sem uso.
3. Consolidar helpers apenas quando sua semântica for idêntica.
4. Preservar tratamento defensivo de erro/autorização.

### Website

1. Remover scaffolding, dependência, componente, asset ou script que não participe do export/site publicado.
2. Preservar links/artefatos de release e coerência com a distribuição.
3. Não transformar a limpeza em redesign da landing page.

### Gate da fase

Executar testes/lint/typecheck/build/audit definidos pelos `package.json` de cada superfície alterada.

---

## Fase 9 — Sweep transversal: dependências, testes, configs, recursos, docs e automação

**Risco:** alto.
**Natureza:** limpeza repo-wide depois que a arquitetura já estabilizou.

### Objetivo

Capturar o deadcode que só se torna visível depois das fases estruturais e remover resíduos que atravessam múltiplas camadas.

### Dependências e projeto

1. Auditar todos os `PackageReference`, `ProjectReference` e versões centralizadas.
2. Remover NuGet sem consumidor real.
3. Auditar dependências npm de Worker/dashboard/site.
4. Remover npm package/script sem uso comprovado.
5. Auditar `.config`, `global.json`, `.node-version`, `Directory.Build.props`, `Directory.Packages.props` e configuração de build.
6. Preservar Central Package Management e baseline de toolchain vigente salvo decisão técnica explícita separada.

### Testes

1. Encontrar testes de código que já não existe.
2. Remover teste somente junto com o comportamento morto que ele validava.
3. Identificar helpers/fixtures/mocks sem consumidor.
4. Adicionar characterization/regression test antes de remover código complexo quando a intenção atual não estiver suficientemente protegida.
5. Não perseguir número de cobertura artificial; perseguir risco real e contrato importante.

### Configuração, recursos e localização

1. Sweeping final de `.resx`, XAML keys, themes, templates, converters e assets.
2. Remover config keys/env vars sem leitor.
3. Remover flags/features internas abandonadas.
4. Confirmar que nenhum recurso carregado por convenção foi apagado.

### Scripts, CI e installer

1. Remover função/script/job/step sem caller.
2. Consolidar automações equivalentes quando isso não esconder diferenças intencionais entre CI, release, installer e portable.
3. Não remover gate de segurança só porque ele parece redundante; provar a redundância pelo fluxo completo.

### Documentação

1. Atualizar `docs/architecture.md` e docs especializadas para a arquitetura final.
2. Remover documentação operacional comprovadamente obsoleta.
3. Não tratar `PROJECT_HISTORY.md`, changelog publicado ou relatórios históricos `.ai/tasks/` como deadcode de produção. Histórico deliberado não é lixo.
4. Tarefas isoladas seguem as regras de `AI_RULES.md` sobre `PROJECT_STATE.md`.

### Gate da fase

Depois deste sweep, toda dependência, projeto, recurso e automação relevante deve ter um motivo observável para continuar existindo.

---

## Fase 10 — Verificação final, auditoria de arquitetura e relatório de remoção

**Risco:** CRÍTICO como gate final.
**Natureza:** nenhuma nova refatoração ampla; corrigir apenas regressões ou resíduos comprovados.

### 10.1 Novo censo de dead code

Repita o inventário da Fase 1 sobre o estado final e compare:

- candidatos removidos;
- candidatos consolidados;
- `SUSPICIOUS` mantidos com justificativa;
- itens `LEGACY-LIVE` mantidos por compatibilidade;
- novas duplicações acidentalmente introduzidas durante a própria refatoração.

### 10.2 Validação .NET

Quando aplicável ao estado atual:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
dotnet format FiveMCleaner.slnx --verify-no-changes
.\scripts\Verify-Safety.ps1
git diff --check
```

A baseline registrada no `PROJECT_STATE.md` é apenas referência histórica. **Use os números obtidos na branch atual, nunca repita números antigos como se fossem prova atual.**

### 10.3 Validação web/infra

Para cada superfície alterada:

- Worker: testes + audit e validações versionadas;
- dashboard: testes/lint/build/audit conforme scripts existentes;
- website: lint/typecheck/test/build/export/audit conforme scripts existentes.

### 10.4 Distribuição

Quando aplicável e sem publicação remota:

- validar portable;
- validar installer;
- validar manifesto/checksums/SBOM e contratos de release locais;
- validar fluxo de updater por testes/simulações suportadas;
- validar modo demo/smoke do App.

### 10.5 Revisão arquitetural final

Confirmar explicitamente:

- App não ganhou dependência de privilégio;
- Contracts não conhece implementação/UI;
- Core não ganhou detalhes Windows/UI desnecessários;
- Windows não decide perfil de produto;
- Broker permanece mínimo e allowlisted;
- updater/distribuição continuam transacionais e verificáveis;
- Worker/dashboard/site têm contratos coerentes;
- nenhum ciclo de dependência foi introduzido;
- nenhum erro está sendo silenciado apenas para simplificar código.

### 10.6 Relatório final

Produzir um relatório final contendo:

- arquivos removidos;
- tipos/métodos/resources/endpoints/scripts removidos;
- dependências removidas;
- duplicações consolidadas;
- classes/fluxos simplificados;
- mudanças de fronteira arquitetural;
- candidatos suspeitos deliberadamente preservados e motivo;
- resultado de todos os gates;
- riscos ou dívida técnica restante;
- qualquer mudança comportamental inevitável, com justificativa e teste.

---

## 6. Critério global de sucesso

A refatoração é considerada concluída somente se:

- o projeto mantém o comportamento suportado atual;
- segurança, privilégio mínimo e rollback não foram enfraquecidos;
- o catálogo de Actions está coerente com as implementações reais;
- não há código/recursos/configuração comprovadamente mortos nas áreas auditadas;
- dependências e automações restantes possuem consumidor/propósito verificável;
- nenhuma camada ficou mais genérica ou mais acoplada apenas para reduzir linhas;
- a suíte completa aplicável passa sem warnings novos;
- documentação arquitetural relevante representa o código final;
- o relatório final permite entender exatamente **o que foi removido, por quê e com qual evidência**.

O objetivo não é atingir “zero linhas suspeitas” por força bruta. O objetivo é chegar ao menor sistema que preserve de forma comprovável o produto, os contratos, a segurança e a capacidade de evolução.

---

## 7. Ordem obrigatória

```text
Fase 0  → Fundação/Contracts/Core (já enviada)
   ↓
Fase 1  → Inventário global e censo de deadcode
   ↓
Fase 2  → Windows Infrastructure / sensores / primitivas
   ↓
Fase 3  → Transaction Engine / Journal / Runtime / Rollback
   ↓
Fase 4  → Action Catalog / implementações / regras
   ↓
Fase 5  → App WPF / Services / ViewModels / recursos
   ↓
Fase 6  → Broker / IPC / privilégio
   ↓
Fase 7  → Launcher / Updater / UpdateRuntime / ReleaseTool / distribuição
   ↓
Fase 8  → Worker / Dashboard / Website
   ↓
Fase 9  → Sweep transversal de deps/testes/config/docs/automação
   ↓
Fase 10 → Verificação total e relatório final
```

**Não pule fases e não rode duas fases estruturalmente dependentes em paralelo.** Uma fase só vira baseline da seguinte depois de validada e integrada.
