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

Toda tarefa concluída termina automaticamente, sem que o usuário precise
pedir, com os passos abaixo. Um commit local **não** é uma operação remota e
não exige autorização — é parte obrigatória de finalizar qualquer tarefa.

1. Revise todas as alterações e confirme que não há arquivos acidentais,
   segredos, builds, caches ou dados locais no escopo do commit.
2. Execute os testes disponíveis, build, lint e typecheck quando existirem e
   forem aplicáveis à área modificada.
3. Corrija os erros introduzidos pela própria tarefa antes de concluir.
4. Crie automaticamente um único commit Git local, claro, objetivo e
   profissional, para a tarefa concluída.

## Branch de trabalho

Todo o desenvolvimento acontece na branch de desenvolvimento vigente (por
exemplo, `dev/proxima-versao`). A branch `main` representa exclusivamente
versões públicas já publicadas do aplicativo e só é atualizada durante uma
**publicação oficial** (ver seção própria).

## Operações remotas

Um commit local nunca autoriza, por si só, qualquer operação remota. Toda
operação remota (push, criação de release, publicação de site, deploy) exige
autorização explícita do usuário nesta tarefa, e só pode ser uma das duas
categorias abaixo — nunca uma operação remota "genérica" fora delas.

### Push de desenvolvimento

Disparado quando o usuário autoriza explicitamente um **"push de
desenvolvimento"** (ou frase equivalente inequívoca). Ao receber essa
autorização, a IA deve automaticamente:

- enviar (`git push`) **apenas** a branch de desenvolvimento atual (a branch
  em que o trabalho está, nunca `main`);
- **nunca** enviar ou tocar a branch `main`;
- **nunca** criar Pull Request;
- **nunca** criar tag;
- **nunca** alterar a versão do aplicativo em nenhum arquivo (projeto,
  assemblies, instalador, manifestos, site, `CHANGELOG.md` ou metadados);
- **nunca** gerar ou publicar release;
- **nunca** atualizar, gerar ou publicar o instalador;
- **nunca** fazer deploy ou publicar/atualizar o site;
- **nunca** atualizar o changelog de versão pública.

Finalidade exclusiva do push de desenvolvimento: **backup remoto**,
**sincronização entre agentes de IA** (por exemplo, Claude Code e Codex
trabalhando alternadamente no mesmo projeto) e **continuidade do
desenvolvimento**. Não representa, em nenhuma hipótese, uma publicação.

Preserve integralmente o histórico ao enviar a branch de desenvolvimento: não
faça squash, não reescreva commits existentes e não descarte trabalho já
commitado, a menos que o usuário peça isso de forma explícita e inequívoca.

### Publicação oficial

Disparada **somente** quando o usuário usa uma frase equivalente a:
"publicar versão", "lançar versão", "criar release", "publicar atualização"
ou "fazer release oficial". Fora dessas frases, nenhuma ação de publicação
deve ocorrer, mesmo que uma branch de desenvolvimento já tenha sido enviada.

Ao ser disparada, a IA deve:

1. Revisar completamente o projeto (código, testes, documentação relevante).
2. Validar build e testes; corrigir falhas antes de prosseguir.
3. Calcular automaticamente a próxima versão usando
   [Semantic Versioning](https://semver.org/lang/pt-BR/) — ver critérios
   abaixo. Não delegar essa decisão ao usuário nem incrementar números de
   forma arbitrária.
4. Atualizar todos os arquivos de versão (projeto/app, assemblies,
   instalador, manifestos, pacote portátil, metadados de release, workflows,
   atualizador, site, README e demais arquivos de distribuição), sem deixar
   números divergentes.
5. Atualizar `CHANGELOG.md` e as notas de release com um resumo fiel às
   mudanças reais implementadas e validadas pelos testes.
6. Atualizar o instalador, o site e demais artefatos de distribuição.
7. Fazer merge da branch de desenvolvimento para `main`, quando necessário.
8. Criar a tag correspondente à nova versão.
9. Publicar oficialmente: `git push` de `main` e da tag, e demais publicações
   de artefatos (site, release, instalador).

Um push autorizado não autoriza ocultar falhas: se build, testes, lint,
typecheck, empacotamento ou validação da versão falharem, a IA deve corrigir o
problema antes do push ou informar claramente que a publicação ficou
bloqueada.

#### Classificação de versão (Semantic Versioning)

- **patch** (`X.Y.Z` → `X.Y.(Z+1)`): correções, ajustes visuais, segurança,
  documentação de release ou melhorias internas compatíveis que não adicionam
  uma capacidade pública relevante;
- **minor** (`X.Y.Z` → `X.(Y+1).0`): novas funcionalidades públicas
  compatíveis, fluxos relevantes adicionais ou melhorias de produto que ampliam
  a capacidade sem quebrar integrações existentes;
- **major** (`X.Y.Z` → `(X+1).0.0`): mudança incompatível de contrato,
  instalação, atualização, dados persistidos ou comportamento público que exija
  migração, atenção explícita ou perda de compatibilidade.

Incrementar somente o último componente numérico enquanto possível:
`1.0.0` → `1.0.1` → ... → `1.0.99` → `1.1.0` → `1.1.1` → ... A mesma regra
vale para todos os componentes posteriores (`1.2.99` → `1.3.0`, e assim por
diante).

O bloco **Últimas atualizações** deve refletir somente alterações realmente
presentes no commit e na release, sem inventar correções ou prometer
resultados não testados. Formato:

```text
Últimas atualizações:
Versão 1.2.3

- Corrigido: descrição objetiva da correção.
- Melhorado: descrição objetiva da melhoria.
- Atualizado: descrição objetiva de dependências, componentes ou dados.
```

Alterações exclusivamente em `AI_RULES.md` ou em outra documentação de
governança de IA podem ser enviadas por push de desenvolvimento, sem criar
uma nova versão pública; nunca devem ser apresentadas como alteração do
aplicativo.

## Fluxo de trabalho

```text
Implementação → Testes → Commit local automático → Nova tarefa →
Commit local automático → [usuário pede "push de desenvolvimento"] →
Push apenas da branch de desenvolvimento → Continuar desenvolvendo
normalmente → [Somente quando o usuário pedir uma publicação oficial] →
Preparar release completa → Merge para main → Tag → Release →
Publicação da nova versão
```
