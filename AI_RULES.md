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

## Versionamento obrigatório ao fazer push

### Diferença entre commit local e push público

- Enquanto o usuário não disser explicitamente **push**, a IA deve apenas
  trabalhar localmente e criar commits conforme as regras do projeto. Esses
  commits não representam uma nova versão pública e não devem publicar,
  substituir ou anunciar instaladores.
- Quando o usuário disser **push**, a IA deve preparar uma publicação completa
  usando o estado real e verificável do projeto naquele momento. A versão
  informada deve ser exatamente a versão que foi gravada nos arquivos do
  projeto, no executável/app incluído no pacote, no instalador, nos manifestos,
  checksums, tags, release, site e demais metadados.
- O push deve atualizar publicamente todos os artefatos correspondentes àquela
  versão, incluindo o aplicativo que vai dentro do instalador. Nunca publicar
  um instalador antigo com número novo, um app diferente do código informado,
  hashes incorretos, notas genéricas ou detalhes inventados.
- As notas de **Últimas atualizações** devem ser geradas a partir das mudanças
  reais presentes desde a última publicação e só podem afirmar o que foi
  implementado e validado pelos testes executados.

Quando o usuário autorizar ou solicitar um `git push`, a IA deve tratar esse
push como uma nova versão pública do aplicativo. Antes do push, ela deve:

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
