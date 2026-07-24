# Regras para IAs

## Colaboração

Este projeto é desenvolvido alternadamente por diferentes IAs e por pessoas.
Cada agente deve preservar o contexto e o trabalho já existente, mesmo quando
não tiver sido produzido por ele.

## Antes de alterar código ou documentação

1. Leia integralmente `PROJECT_STATE.md`.
2. Analise o estado atual do Git (`git status`, histórico recente e diferenças
   relevantes).
3. Inspecione o código-fonte relacionado à tarefa e os testes existentes antes
   de propor ou aplicar uma mudança.
4. Trate o código-fonte e o Git como a fonte principal da verdade. A
   documentação é contexto e pode estar defasada.
5. Nunca desfaça, sobrescreva ou descarte alterações anteriores sem primeiro
   entender sua motivação, impacto e autoria disponível no histórico.

## Durante a implementação

- Preserve os limites de segurança documentados em `docs/safety.md` e a
  separação arquitetural descrita em `docs/architecture.md`.
- Mantenha cada tarefa como uma unidade lógica; não misture refatorações ou
  limpezas não relacionadas.
- Não crie commits para experimentos, tentativas intermediárias ou trabalho
  incompleto.
- Atualize `PROJECT_STATE.md` somente quando houver mudança relevante de
  arquitetura, decisão técnica, funcionalidade entregue ou limitação conhecida.

## Ao concluir uma tarefa

1. Revise todas as alterações e confirme que não há arquivos acidentais,
   segredos, builds, caches ou dados locais no escopo do commit.
2. Execute os testes disponíveis, build, lint e typecheck quando existirem e
   forem aplicáveis à área modificada.
3. Corrija os erros introduzidos pela própria tarefa antes de concluir.
4. Faça um único commit Git claro, objetivo e profissional para a tarefa
   concluída.

## Operações remotas

Nunca execute `git push`, crie releases, publique site, acione deploy ou faça
qualquer outra publicação remota sem autorização explícita do usuário nesta
tarefa. Um commit local não autoriza publicação.

Nem todo `git push` autorizado é uma publicação. Este documento distingue
explicitamente dois tipos de push remoto, tratados por regras diferentes:

- **push de desenvolvimento** (ver seção própria abaixo): envia uma branch de
  desenvolvimento/sincronização para o remoto, sem tocar `main` nem criar tag.
  Não é publicação e não aciona o procedimento de versionamento.
- **publicação oficial**: push que atualiza `main` e/ou cria uma tag/release
  pública. Aciona integralmente o procedimento descrito em "Versionamento
  obrigatório ao fazer push".

Antes de qualquer push, a IA deve identificar explicitamente qual dos dois
tipos o usuário está autorizando nesta tarefa e seguir somente as regras
correspondentes. Na dúvida, tratar como push de desenvolvimento (o mais
conservador) e perguntar antes de aplicar qualquer efeito de publicação.

## Push de desenvolvimento

Um push de desenvolvimento serve exclusivamente para **backup remoto,
sincronização e continuidade entre diferentes agentes de IA** (por exemplo,
Claude Code e Codex trabalhando alternadamente no mesmo projeto) — não para
disponibilizar uma nova versão ao público.

Um push é de desenvolvimento quando todas as condições abaixo são
verdadeiras:

- o destino é uma branch que não é `main` (por exemplo, `dev/*`,
  `feature/*` ou equivalente combinada com o usuário nesta tarefa);
- nenhuma tag é criada ou enviada;
- o usuário não pediu explicitamente uma release, publicação ou nova versão
  pública nesta tarefa.

Quando essas condições são atendidas, o push de desenvolvimento:

- **não representa publicação** de nenhuma forma, ainda que o código enviado
  esteja completo e validado;
- **não** deve alterar a versão do aplicativo em nenhum arquivo (projeto,
  assemblies, instalador, manifestos, site, `CHANGELOG.md` ou metadados);
- **não** deve gerar release nem abrir rascunho de release no GitHub;
- **não** deve criar tag;
- **não** deve atualizar, gerar ou publicar o instalador;
- **não** deve publicar ou atualizar o site;
- **não** deve acionar deploy nem qualquer workflow de distribuição;
- **não** autoriza merge para `main`, nem abertura de Pull Request, a menos
  que o usuário peça isso explicitamente como uma ação separada.

Mesmo sendo uma operação de baixo risco de publicação, um push de
desenvolvimento continua sendo uma operação remota: exige autorização
explícita do usuário nesta tarefa, igual a qualquer outro item da seção
"Operações remotas". A autorização de um push de desenvolvimento não deve
ser interpretada como autorização para nenhuma das ações listadas acima;
cada uma delas exige seu próprio pedido explícito, feito em outra tarefa.

Preserve integralmente o histórico ao criar ou atualizar uma branch de
desenvolvimento: não faça squash, não reescreva commits existentes e não
descarte trabalho já commitado, a menos que o usuário peça isso de forma
explícita e inequívoca.

## Versionamento obrigatório ao fazer push

As regras desta seção (e das subseções abaixo) aplicam-se exclusivamente à
**publicação oficial** — push que atualiza `main` e/ou cria tag/release
pública. Elas não se aplicam a um push de desenvolvimento, que segue apenas
a seção "Push de desenvolvimento" acima.

### Responsabilidade de versão e Semantic Versioning

As IAs que trabalham neste projeto são responsáveis por definir a próxima versão
pública com base no estado real do produto e em
[Semantic Versioning](https://semver.org/lang/pt-BR/). Não delegue essa decisão
ao usuário nem incremente números de forma arbitrária:

- **patch** (`X.Y.Z` → `X.Y.(Z+1)`): correções, ajustes visuais, segurança,
  documentação de release ou melhorias internas compatíveis que não adicionam
  uma capacidade pública relevante;
- **minor** (`X.Y.Z` → `X.(Y+1).0`): novas funcionalidades públicas
  compatíveis, fluxos relevantes adicionais ou melhorias de produto que ampliam
  a capacidade sem quebrar integrações existentes;
- **major** (`X.Y.Z` → `(X+1).0.0`): mudança incompatível de contrato,
  instalação, atualização, dados persistidos ou comportamento público que exija
  migração, atenção explícita ou perda de compatibilidade.

Ao preparar uma publicação, a IA deve justificar internamente a classificação,
aplicar a versão escolhida de forma consistente no app, instalador, artefatos,
site, changelog, README, tag, release e metadados, e validar essa coerência
antes de publicar. Alterações exclusivamente neste `AI_RULES.md` ou em outra
documentação de governança de IA podem ser enviadas sem criar uma nova versão
pública; nunca devem, porém, ser apresentadas como alteração do aplicativo.

### Diferença entre commit local, push de desenvolvimento e publicação oficial

- Enquanto o usuário não autorizar explicitamente um push para `main` e/ou a
  criação de tag/release, a IA deve apenas trabalhar localmente e criar
  commits conforme as regras do projeto (ou, quando autorizado à parte, um
  push de desenvolvimento — ver seção própria). Nenhum desses dois casos
  representa uma nova versão pública, e nenhum deles deve publicar,
  substituir ou anunciar instaladores.
- Quando o usuário autorizar especificamente uma **publicação oficial** —
  push para `main` combinado com tag/release, não um push de desenvolvimento
  isolado —, a IA deve preparar uma publicação completa usando o estado real
  e verificável do projeto naquele momento. A versão informada deve ser
  exatamente a versão que foi gravada nos arquivos do projeto, no
  executável/app incluído no pacote, no instalador, nos manifestos,
  checksums, tags, release, site e demais metadados.
- A publicação deve atualizar publicamente todos os artefatos correspondentes
  àquela versão, incluindo o aplicativo que vai dentro do instalador. Nunca
  publicar um instalador antigo com número novo, um app diferente do código
  informado, hashes incorretos, notas genéricas ou detalhes inventados.
- As notas de **Últimas atualizações** devem ser geradas a partir das mudanças
  reais presentes desde a última publicação e só podem afirmar o que foi
  implementado e validado pelos testes executados.

Quando o usuário autorizar ou solicitar explicitamente uma publicação oficial
(não um push de desenvolvimento), a IA deve tratar essa publicação como uma
nova versão pública do aplicativo. Antes de publicar, ela deve:

1. Ler a versão atual na fonte de verdade do projeto e calcular a próxima
   versão permitida.
2. Incrementar somente o último componente numérico enquanto possível:
   `1.0.0` → `1.0.1` → ... → `1.0.99` → `1.1.0` → `1.1.1` → ... .
   A mesma regra vale para todos os componentes posteriores (`1.2.99` →
   `1.3.0`, e assim por diante).
3. Atualizar consistentemente a versão em todos os locais aplicáveis, sem
   deixar números divergentes: projeto/app, assemblies, instalador, manifestos,
   pacote portátil, metadados de release, workflows, atualizador, site,
   README, CHANGELOG e demais arquivos de distribuição.
4. Executar os validadores de progressão de versão e confirmar que a tag,
   artefatos e metadados usam exatamente a mesma versão.
5. Atualizar no GitHub o bloco visível **Últimas atualizações**, mantendo-o
   organizado e no topo da informação de release/documentação. O formato deve
   ser semelhante a:

   ```text
   Últimas atualizações:
   Versão 1.2.3

   - Corrigido: descrição objetiva da correção.
   - Melhorado: descrição objetiva da melhoria.
   - Atualizado: descrição objetiva de dependências, componentes ou dados.
   ```

   Esse bloco deve refletir somente alterações realmente presentes no commit
   e na release, sem inventar correções ou prometer resultados não testados.
6. Atualizar `CHANGELOG.md` e as notas da release com o mesmo resumo, incluindo
   a versão exata, correções, melhorias e atualizações relevantes.
7. Fazer um único commit profissional contendo a unidade completa de trabalho,
   criar a tag correspondente quando a publicação for autorizada e somente
   então executar o `git push` da branch e da tag. Nunca fazer push de uma
   versão parcialmente atualizada.

Um push autorizado não autoriza ocultar falhas: se build, testes, lint,
typecheck, empacotamento ou validação da versão falharem, a IA deve corrigir o
problema antes do push ou informar claramente que a publicação ficou bloqueada.
