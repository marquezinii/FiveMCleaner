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
