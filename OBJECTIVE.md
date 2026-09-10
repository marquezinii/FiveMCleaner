# Objetivo

# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Auditar e fortalecer os fluxos de conta, autenticação e persistência de dados.
- **Escopo:** Cadastro, login, sessão, perfil, recuperação, autorização, armazenamento local e rotas Worker/D1 relacionadas. Não inclui mudança de produto, release ou infraestrutura não relacionada.
- **Critérios de conclusão:** Fluxos críticos revisados ponta a ponta, validações e testes relevantes executados, sem credenciais ou dados pessoais no repositório.
- **Resultado entregue:** O Worker rejeita payloads de perfil fora do contrato e distingue conflitos de username por estado persistido, sem mensagens do D1. A sessão substitui o token persistido atomicamente, coalesce renovações simultâneas e restaura o estado em cancelamento. O preenchimento assíncrono não cruza dados entre contas trocadas; a bandeja integrada permanece independente desses fluxos.
