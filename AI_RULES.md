# Regras para IAs

## Princípio de operação

Este projeto pode ser desenvolvido simultaneamente por pessoas e por múltiplos
agentes de IA. O usuário não deve precisar administrar Git para o fluxo normal:
ao receber uma tarefa de desenvolvimento, auditoria, correção, refatoração, UI,
segurança ou equivalente, o agente descobre e executa automaticamente os passos
mecânicos abaixo. Só peça orientação quando houver uma decisão real de produto,
comportamento, segurança ou uma ambiguidade que não possa ser resolvida com
segurança pelo estado do projeto.

Código-fonte e Git são a fonte principal da verdade; a documentação fornece
contexto e pode estar defasada. Preserve sempre o contexto e o trabalho já
existente, mesmo que não tenham sido produzidos pelo agente atual.

## Branches e isolamento

- `main` contém exclusivamente versões públicas já publicadas e só é alterada
  durante uma **publicação oficial**.
- `dev/proxima-versao` é a branch oficial de **integração** da próxima versão.
  Agentes de tarefas normais não desenvolvem diretamente nela.
- `ai/<agente>/<tarefa>` é uma branch temporária e exclusiva de uma tarefa.
  O formato preferencial é, por exemplo, `ai/codex/backend-refactor` ou
  `ai/claude/security-audit`. Use a identidade conhecida do agente; se ela não
  puder ser determinada, use `ai/agent/<tarefa>`. Gere automaticamente um slug
  curto e apropriado; se já existir, gere uma variante única sem perguntar.

Para cada nova tarefa normal, o agente deve, automaticamente:

1. identificar a raiz do repositório e ler integralmente `AI_RULES.md` e
   `PROJECT_STATE.md`;
2. verificar `git status`, histórico recente, branches e worktrees existentes;
3. usar `dev/proxima-versao` como base, criando sua branch exclusiva;
4. criar um Git worktree exclusivo em um diretório irmão do repositório
   principal, com nome determinado automaticamente;
5. executar alterações, testes e commits somente nesse worktree, sem trocar a
   branch do checkout compartilhado.

Se o agente já estiver em um worktree exclusivo da sua própria tarefa, deve
reutilizá-lo. Caso um worktree não seja tecnicamente possível, o agente deve
preservar o checkout compartilhado, evitar trocar sua branch e informar a
limitação antes de prosseguir por uma alternativa segura.

## Segurança no trabalho concorrente

Assuma sempre que outros agentes podem estar trabalhando simultaneamente.

- Nunca apagar, resetar, descartar, sobrescrever ou sincronizar trabalho de
  outra branch. Nunca usar `git reset --hard` em trabalho que possa pertencer a
  outro agente, nem force push.
- Nunca modificar arquivos sem relação com a tarefa por limpeza estética;
  evite reformatações massivas e refatorações oportunistas não relacionadas.
- Limite as mudanças ao escopo solicitado e preserve o comportamento existente
  fora dele.
- Resolva conflitos conscientemente durante a integração; nunca os oculte com
  `ours`, `theirs` ou sobrescrita indiscriminada.
- Preserve os limites de segurança em `docs/safety.md` e a separação
  arquitetural em `docs/architecture.md`.
- Antes de alterar código ou documentação relacionada, inspecione os arquivos e
  testes afetados. Nunca desfaça alterações anteriores sem compreender sua
  motivação, impacto e autoria disponível no histórico.

## Relatório e conclusão da tarefa

`PROJECT_STATE.md` é o estado **oficial** do projeto e não deve ser alterado
por tarefas paralelas — nem mesmo para registrar o próprio progresso, uma
descoberta ou um achado de auditoria. Um agente em tarefa isolada que editar
`PROJECT_STATE.md` diretamente cometeu um erro de processo e deve ser
corrigido antes da integração; o agente integrador não deve levar essa edição
adiante, mesmo que o conteúdo em si seja válido — o conteúdo técnico deve ser
reincorporado via `.ai/tasks/` e só chega a `PROJECT_STATE.md` pela mão do
integrador. Durante uma tarefa isolada, crie ou atualize somente o
relatório exclusivo `.ai/tasks/<identificador-da-tarefa>.md`, usando um
identificador único derivado da tarefa. Ele deve registrar, de forma curta:

- agente, branch, objetivo e status;
- resumo das mudanças e arquivos/áreas principais alterados;
- testes executados e resultados;
- decisões relevantes, bugs ou limitações restantes;
- commits criados e observações importantes para integração.

Uma tarefa concluída termina automaticamente, sem pedido adicional do usuário:

1. revise as mudanças e confirme que não há arquivos acidentais, segredos,
   builds, caches ou dados locais no commit;
2. execute testes, build, lint e typecheck disponíveis e aplicáveis;
3. corrija os erros introduzidos pela própria tarefa;
4. atualize o relatório exclusivo da tarefa;
5. crie automaticamente um único commit Git local, claro e profissional;
6. reconstrua o atalho `FiveMCleaner - Desenvolvimento` da área de trabalho
   com `scripts\Install-DevelopmentShortcut.ps1 -Build`, para que o usuário
   consiga abrir e ver imediatamente o resultado de qualquer alteração feita
   por uma IA, em qualquer tarefa, branch ou worktree. Se o script falhar ou
   não puder ser executado no ambiente atual, informe explicitamente que o
   atalho não foi reconstruído e por quê, em vez de omitir o passo;
7. deixe a branch pronta para integração e informe branch, commit, testes e o
   status **pronto para integração**.

Commits locais são obrigatórios e não exigem autorização remota. Não crie
commits para experimentos, tentativas intermediárias ou trabalho incompleto.
Não faça merge automático em `dev/proxima-versao` ao concluir uma tarefa.

### Padrão de mensagens de commit

Toda mensagem de commit segue [Conventional Commits](https://www.conventionalcommits.org/pt-br/):
`tipo(escopo opcional): descrição curta no imperativo`, por exemplo
`fix(worker): corrige rate limit da rota de telemetria` ou
`docs: atualiza README com nova estrutura de pastas`. Tipos comuns: `feat`,
`fix`, `docs`, `refactor`, `test`, `chore`, `ci`, `perf`, `build`, `revert`.

- Nunca gere mensagens genéricas ou de artefato de build (ex.: `Build
  FiveMCleaner vX.Y MVP`, `WIP`, `update`, `fix stuff`) — cada commit deve
  descrever a decisão real, não o processo que o produziu.
- Não invente número de versão na mensagem; a versão só muda durante uma
  publicação oficial (ver seção correspondente).
- Corpo opcional em linhas adicionais quando o contexto não couber no título;
  sem menção a nomes de agente de IA, prompt ou ferramenta interna.
- Este padrão vale a partir de agora; commits antigos já publicados
  (inclusive os de releases já publicadas) não são reescritos — reescrever
  histórico publicado quebraria tags de release existentes e é proibido pela
  seção "Segurança no trabalho concorrente" acima, salvo pedido explícito e
  inequívoco do usuário ciente do risco.

Exceção: uma tarefa explicitamente autorizada como integração, ou uma tarefa
não concorrente explicitamente autorizada diretamente em `dev/proxima-versao`,
pode atualizar `PROJECT_STATE.md`.

## Integração das tarefas

Frases como “integrar trabalhos”, “integrar as IAs”, “integrar branches”,
“integrar tarefas concluídas” ou “preparar a dev com os trabalhos concluídos”
ativam o modo **agente integrador**. Nesse modo, o agente deve:

1. analisar `dev/proxima-versao`, as branches `ai/*` relevantes e os relatórios
   em `.ai/tasks/`;
2. identificar quais tarefas estão efetivamente concluídas e prontas;
3. determinar uma ordem lógica quando houver dependências;
4. integrar uma branch por vez em `dev/proxima-versao`, examinando e resolvendo
   conflitos para preservar mudanças válidas dos dois lados;
5. testar após integrações relevantes, corrigir incompatibilidades e executar a
   suíte completa aplicável ao final;
6. atualizar `PROJECT_STATE.md` com o estado oficial já integrado, criar o
   commit de integração quando necessário e marcar os relatórios integrados.
7. atualizar a simulação local da próxima versão com
   `scripts\Install-DevelopmentShortcut.ps1 -Build` e confirmar que o atalho
   `FiveMCleaner - Desenvolvimento` aponta para
   `scripts\Start-DevelopmentApp.ps1`. Esse atalho recompila o Release atual
   antes de abrir o app; portanto ele deve ser usado para validar toda mudança
   integrada, enquanto `FiveMCleaner.lnk` continua sendo a instalação pública.

Quando o usuário pedir que o agente integrador examine branches e worktrees
além de `main` e `dev/proxima-versao`, valide os relatórios e integre todas as
tarefas concluídas aplicáveis. Se o pedido disser para integrar em
`origin/dev/proxima-versao` (ou usar expressão equivalente), ele também é uma
autorização explícita para enviar somente `dev/proxima-versao` ao remoto, após
os testes e a reconstrução obrigatória do atalho. Assim, o atalho
`FiveMCleaner - Desenvolvimento` deve sempre ser reconstruído para refletir o
estado final integrado de `dev/proxima-versao` antes desse push; nunca deve
apontar para `main`, uma branch `ai/*` ou a instalação pública.

Se duas tarefas conflitarem conceitualmente, analise os dois objetivos e
preserve ambos sempre que isso for seguro e coerente. Não descarte uma solução
válida sem análise explícita.

Após uma tarefa estar comprovadamente integrada e validada, seu worktree pode
ser removido e sua branch local temporária pode ser removida se não for mais
necessária. Nunca remova trabalho não integrado nem branch remota sem
autorização explícita.

## Operações remotas

Um commit local nunca autoriza por si só push, release, publicação de site ou
deploy. Toda operação remota exige autorização explícita do usuário nesta
tarefa, exceto o push automático da branch exclusiva descrito abaixo.

### Push automático da branch exclusiva

Ao concluir uma tarefa em uma branch `ai/<agente>/<tarefa>`, o agente pode
fazer push automático dessa branch para o remoto sem autorização explícita do
usuário. Esse push é restrito exclusivamente à branch da tarefa atual; jamais
é permitido fazer push automático para `main`, `dev/proxima-versao` ou
qualquer outra branch sem permissão explícita do usuário.

Condições para o push automático:

- a branch deve seguir o padrão `ai/<agente>/<tarefa>` e ser de uso exclusivo
  do agente atual;
- o agente deve ter concluído todos os passos de conclusão da tarefa (testes,
  lint, commit, relatório);
- o push envia somente essa branch, sem alterar refs remotas de outras
  branches;
- se o push remoto falhar (conflito, rejeição, erro de rede), o agente deve
  informar o usuário e não tentar forçar o push.

### Push de desenvolvimento

É disparado somente por “push de desenvolvimento” ou equivalente inequívoco,
ou automaticamente ao concluir uma tarefa na branch exclusiva conforme
descrito acima.

- Se o agente estiver em `ai/*`, envie somente essa branch.
- Se estiver no modo integrador em `dev/proxima-versao`, envie somente
  `dev/proxima-versao`.
- Nunca envie ou altere `main`, crie Pull Request, tag ou release.
- Nunca altere versão, `CHANGELOG.md` público, instalador, site, artefatos de
  distribuição ou updater nesse fluxo.
- Preserve integralmente o histórico: não faça squash, reescreva commits ou
  descarte trabalho já commitado sem pedido explícito e inequívoco do usuário.

Esse push serve apenas para backup remoto, sincronização entre agentes e
continuidade do desenvolvimento; nunca é publicação.

## Publicação oficial

É disparada somente por frase como “publicar versão”, “lançar versão”, “criar
release”, “publicar atualização” ou “fazer release oficial”. Ela sempre parte
do estado já integrado e consistente de `dev/proxima-versao`; branches `ai/*`
nunca são publicadas diretamente e tarefas paralelas incompletas não entram na
publicação.

Ao ser disparada, a IA deve:

1. revisar completamente o projeto, o histórico integrado e a documentação
   relevante, validando build e testes e corrigindo falhas antes de prosseguir;
2. calcular a próxima versão com [Semantic Versioning](https://semver.org/lang/pt-BR/),
   usando todas as mudanças efetivamente integradas desde a última tag;
3. atualizar todos os arquivos de versão, `CHANGELOG.md`, notas de release,
   instalador, site e demais artefatos de distribuição, sem divergências;
4. fazer merge de `dev/proxima-versao` para `main`, salvo se uma comparação
   explícita de histórico e conteúdo provar que ambas já são idênticas;
5. criar a tag da versão, publicar `main`, a tag, os artefatos oficiais e a
   GitHub Release, cujo corpo segue obrigatoriamente o
   [Padrão das GitHub Releases](#padrão-das-github-releases-release-notes)
   definido abaixo;
6. validar o atualizador de ponta a ponta e sincronizar `dev/proxima-versao`
   com a `main` publicada para iniciar o próximo ciclo.

Um push autorizado não permite ocultar falhas: build, testes, lint, typecheck,
empacotamento e validação de versão devem passar, ou o bloqueio deve ser
informado claramente.

### Sincronização após a publicação

Depois de uma publicação oficial bem-sucedida, `main` e
`dev/proxima-versao` devem apontar para o mesmo conteúdo e histórico. A branch
de integração fica preparada como base das próximas tarefas, que voltarão a
nascer em branches `ai/*` isoladas.

### Validação do atualizador

Antes de considerar qualquer publicação concluída, valide, sempre que possível:

- consulta da fonte de atualizações pelo aplicativo instalado;
- detecção e comparação corretas da nova versão;
- disponibilidade do artefato oficial de instalação/atualização;
- coerência de links, manifestos, hashes e metadados;
- aviso correto ao usuário sobre a atualização disponível.

Quando a validação completa depender de instalação real, rede externa ou
interação manual, relate exatamente o que foi verificado e o que permanece
pendente. Nunca afirme que o atualizador funciona sem evidência concreta.

### Padrão das GitHub Releases (Release Notes)

O corpo (`body`) de toda GitHub Release estável é consumido automaticamente
pelo canal oficial de atualizações do Discord. Por isso, a partir do momento
em que essa automação existir, as Release Notes deixam de ser um detalhe
interno e passam a ser uma **saída pública oficial do projeto**, com padrão
obrigatório.

**Tag e título**

- Tag: `vMAJOR.MINOR.PATCH` (ex.: `v1.4.2`) — sem `v` duplicado, sem espaços
  e sem formato alternativo.
- Título: `FiveMCleaner vMAJOR.MINOR.PATCH` (ex.: `FiveMCleaner v1.4.2`).

**Tipo de release**

Enquanto o projeto usar somente o canal estável: toda release oficial é uma
release normal/stable — nunca marcada como `pre-release`, nunca deixada como
`draft` ao final da publicação. Branches `ai/*` jamais geram release pública
(já coberto em "Publicação oficial" acima).

**Estrutura obrigatória do corpo**

Markdown, usando somente as seções abaixo que forem aplicáveis, sempre nesta
ordem, sem seções vazias (se não houver item real para uma seção, omita a
seção inteira — nunca escreva algo como "Nenhuma alteração"):

```markdown
## ✨ Novidades

- ...

## 🔧 Melhorias

- ...

## 🐛 Correções

- ...

## 🔒 Segurança

- ...

## ⚙️ Alterações técnicas

- ...
```

- `## ✨ Novidades`: novas funcionalidades, novas capacidades públicas,
  recursos percebidos diretamente pelo usuário.
- `## 🔧 Melhorias`: UX, desempenho, confiabilidade, estabilidade,
  refinamento de comportamento já existente.
- `## 🐛 Correções`: bugs, regressões e comportamentos incorretos
  efetivamente corrigidos.
- `## 🔒 Segurança`: correções ou hardening relevantes, validações
  adicionais, mitigação de risco — descritas de forma responsável, sem
  detalhes que facilitem exploração de uma vulnerabilidade ainda relevante.
- `## ⚙️ Alterações técnicas`: refatorações relevantes, dependências,
  arquitetura, build, updater, telemetria, instalador e manutenção técnica
  relevante.

**Regras de conteúdo**

1. Nunca publicar uma release oficial sem Release Notes.
2. Antes de escrever, analise efetivamente todas as mudanças integradas
   desde a última versão pública/tag — as notas devem refletir somente
   mudanças realmente presentes na versão publicada.
3. Nunca invente funcionalidades, melhorias, correções, resultados de teste,
   ganhos de desempenho ou melhorias de segurança; não prometa recursos
   futuros.
4. Não inclua trabalho que ficou só em branches `ai/*` sem integrar, nem
   tarefas canceladas ou experimentais.
5. Escreva sempre em português do Brasil, para o usuário final: claro,
   profissional, objetivo, curto, compreensível, sem jargão interno
   desnecessário. Traduza mensagens de commit cruas (`fix null ref
   AccountVM`, `bump package`, `cleanup`) em descrições públicas
   compreensíveis quando forem relevantes.
6. Cada bullet representa uma mudança concreta; não repita a mesma mudança
   em seções diferentes; agrupe alterações muito pequenas quando fizer
   sentido, mas sem esconder mudanças relevantes.
7. Preserve nomes oficiais de funcionalidades, telas e componentes públicos
   do FiveMCleaner.
8. Nunca inclua hashes de commit, nomes de branch internas, caminhos locais,
   worktrees, prompts, nomes de agentes de IA, detalhes de processo interno,
   segredos, tokens ou dados pessoais.
9. Uma alteração técnica sem impacto ou relevância pública pode permanecer
   só no `CHANGELOG.md` técnico e não precisa aparecer na GitHub Release.

**Relação com o `CHANGELOG.md`**

`CHANGELOG.md` continua sendo o histórico completo e oficial das versões; a
GitHub Release é a apresentação pública resumida e organizada daquela mesma
versão. Os dois devem permanecer coerentes: a Release nunca pode contradizer
o `CHANGELOG.md`, e uma mudança relevante da versão não deve desaparecer das
Release Notes sem motivo. Informação puramente interna pode continuar só no
changelog técnico.

**Integração com Discord**

O corpo da GitHub Release é consumido automaticamente pelo sistema oficial
de notificações do Discord. Por isso:

- não insira um cabeçalho manual como "FiveMCleaner vX.Y.Z está disponível"
  nem repita a versão no início do corpo — o sistema do Discord já cria esse
  cabeçalho a partir do título/tag;
- não adicione links genéricos de download no corpo só para o Discord — a
  automação já anexa o asset da release separadamente;
- nunca use `@everyone`, `@here` ou menções de cargo/usuário;
- evite emojis além dos já padronizados nos títulos das seções;
- evite tabelas Markdown (a apresentação no Discord pode ficar ruim) —
  prefira listas simples com bullets;
- mantenha o Markdown compatível com GitHub e Discord ao mesmo tempo;
- mantenha as notas concisas, mas completas.

**Qualidade antes de publicar**

Antes de criar/publicar a GitHub Release, confirme: versão, tag e título
corretos; Release Notes geradas a partir das mudanças reais; nenhuma seção
vazia; nenhuma mudança inventada ou item relevante omitido; nenhum dado
interno/sensível; coerência entre Release Notes, `CHANGELOG.md`, código
publicado e versão; assets oficiais corretos anexados; release não marcada
como pre-release enquanto o projeto for só stable. Se qualquer verificação
falhar, corrija antes de publicar.

**Exemplo estrutural** (apenas de formato — nunca copie um item dele para
uma release real sem que a mudança correspondente exista de fato):

```markdown
## ✨ Novidades

- Adicionado diagnóstico detalhado das configurações relevantes para o FiveM.
- Adicionada nova visualização das otimizações aplicadas.

## 🔧 Melhorias

- Melhorado o desempenho da análise inicial do sistema.
- Aprimorada a experiência da tela de restauração.

## 🐛 Correções

- Corrigido problema que poderia impedir determinadas otimizações no Windows 11.
- Corrigida inconsistência na exibição do status de algumas ações.

## ⚙️ Alterações técnicas

- Melhorado o tratamento interno de erros e logs.
- Atualizadas dependências utilizadas pelo processo de atualização.
```

### Classificação de versão (Semantic Versioning)

- **patch** (`X.Y.Z` → `X.Y.(Z+1)`): correções, ajustes visuais, segurança,
  documentação de release ou melhorias internas compatíveis sem nova capacidade
  pública relevante;
- **minor** (`X.Y.Z` → `X.(Y+1).0`): novas funcionalidades públicas
  compatíveis ou melhorias de produto que ampliam capacidade sem quebrar
  integrações;
- **major** (`X.Y.Z` → `(X+1).0.0`): mudança incompatível de contrato,
  instalação, atualização, dados persistidos ou comportamento público.

O componente alterado evolui numericamente a partir da versão existente. O
patch é um inteiro decimal SemVer sem largura fixa ou zero à esquerda: `1.1.9`,
`1.1.10`, `1.1.99` e `1.1.100` são válidos. A categoria é decidida pelo
conjunto real de mudanças integradas desde a última versão publicada, não por
uma sequência fixa de increments.

O bloco **Últimas atualizações** deve refletir apenas mudanças presentes no
commit e na release, sem inventar resultados ou prometer itens não testados:

```text
Últimas atualizações:
Versão 1.2.3

- Corrigido: descrição objetiva da correção.
- Melhorado: descrição objetiva da melhoria.
- Atualizado: descrição objetiva de dependências, componentes ou dados.
```

Esse bloco continua sendo usado onde o projeto hoje espera esse formato (por
exemplo, README e site público) e deve ser derivado das mesmas mudanças
reais usadas no `CHANGELOG.md` e nas GitHub Release Notes — nunca pode
divergir da release publicada. As categorias `Corrigido`, `Melhorado` e
`Atualizado` continuam seguindo as regras de conteúdo já definidas acima; ele
não substitui a estrutura de seções (`Novidades`/`Melhorias`/`Correções`/
`Segurança`/`Alterações técnicas`) exigida para o corpo da GitHub Release em
[Padrão das GitHub Releases](#padrão-das-github-releases-release-notes).

Alterações exclusivamente em `AI_RULES.md` ou em outra documentação de
governança podem receber push de desenvolvimento autorizado, sem criar versão
pública; nunca devem ser apresentadas como mudança do aplicativo.

## Fluxo de trabalho

```text
main
→ versão pública

dev/proxima-versao
→ integração oficial da próxima versão

ai/<agente>/<tarefa>
→ trabalho isolado

Nova tarefa
→ leitura de AI_RULES + PROJECT_STATE
→ branch automática da tarefa baseada em dev/proxima-versao
→ worktree exclusivo criado ou reutilizado automaticamente
→ implementação e testes
→ relatório .ai/tasks/
→ commit local automático
→ push automático da branch exclusiva (sem permissão explícita)
→ pronta para integração

Integração solicitada
→ integrar tarefas concluídas em dev/proxima-versao
→ resolver conflitos conscientemente
→ testes completos
→ atualizar PROJECT_STATE
→ dev pronta

Publicação oficial solicitada
→ validações e SemVer
→ changelog e artefatos
→ merge dev/proxima-versao → main
→ tag, push e release
→ validação do updater
→ sincronização main/dev
```
