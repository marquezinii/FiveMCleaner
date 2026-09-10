# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** reduzir trabalho operacional repetitivo com automações de CI, validação e release seguras, proporcionais aos fluxos reais do Ralven.
- **Escopo:** auditar workflows, scripts, versões, localização, documentação derivada, dependências, builds e deploys; implementar somente automações justificadas e não destrutivas. Não inclui publicar release, criar tag, fazer deploy público, alterar `main` ou integrar a própria tarefa.
- **Critérios de conclusão:** automações reutilizam comandos canônicos, têm gatilhos e permissões mínimos, preservam gates de release/deploy, cobrem validações repetitivas identificadas e passam pelos checks aplicáveis.
- **Resultado entregue:** CI consolidada por escopo com gate estável, políticas automatizadas de PR/versão/i18n/roadmap/toolchain/contratos, auditorias semanais, cobertura do site e instalador, release estável acionada por tag com dashboard e Discord integrados, notas geradas do changelog e dependências vulneráveis do site corrigidas. Build, suítes e artefato do instalador foram validados; o ciclo local de instalação ficou bloqueado com diagnóstico explícito por outro instalador Ralven aberto em worktree concorrente.
