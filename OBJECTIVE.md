# Objective

- **Agente:** Codex
- **Objetivo:** Preservar suporte completo e previsível ao Windows 10 no Ralven, inclusive diante das funcionalidades em desenvolvimento.
- **Escopo:** Revisar `dev/proxima-versao` e worktrees ativas em busca de dependências ou comportamentos exclusivos do Windows 11; corrigir incompatibilidades comprovadas e adicionar cobertura proporcional.
- **Fora de escopo:** Alterar suporte a GTAV Enhanced, publicar release ou integrar branches de outros trabalhos.
- **Critérios de conclusão:** APIs e comportamentos incompatíveis identificados possuem fallback seguro ou indisponibilidade explícita; validações automatizadas aplicáveis passam; pontos nativos remanescentes ficam claros para validação manual no Windows 10.
- **Resultado entregue:** O fallback de backdrop centralizado preserva Mica no Windows 11 e usa Acrylic no Windows 10; todas as janelas Fluent usam a política e um teste impede regressão. A revisão da `dev` e das worktrees ativas não encontrou outra API Windows 11-only sem tratamento. Build Release e 1.441 testes passaram; o aceite nativo está documentado em `docs/windows10-validation.md`.
