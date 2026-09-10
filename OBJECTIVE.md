# Objetivo

## Agente

Codex

## Objetivo

Auditar e fortalecer a máquina de estados de conta e autenticação, incluindo autenticação de dois fatores TOTP e recuperação segura de acesso.

## Escopo

Cadastro e login por senha ou Google, vinculação de provedores, verificação e alteração de e-mail, criação/alteração/recuperação de senha, sessões, exclusão de conta, ativação/desativação/login 2FA e códigos de recuperação. Inclui cliente desktop, Worker/D1, documentação e testes diretamente relacionados; não inclui deploy, release ou publicação.

## Critérios de conclusão

- Transições válidas funcionam de ponta a ponta e transições inválidas falham fechadas.
- Ações sensíveis exigem identidade recente e preservam a segurança de sessões.
- 2FA TOTP cobre ativação, confirmação, desafio de login, desativação e recuperação sem armazenar segredos no cliente.
- Testes focados e validações completas aplicáveis passam.
- Nenhum segredo, token ou dado pessoal é adicionado ao repositório.

## Resultado entregue

Máquina de estados de conta corrigida para senha, Google, verificação e troca de
e-mail, recuperação, reautenticação, sessões e exclusão. Foi adicionado 2FA TOTP
com ativação confirmada, desafio de login, desativação, regeneração e consumo
único de códigos de recuperação. O Worker agora aplica autenticação recente,
revogação server-side, rate limiting fail-closed e exclusão durável com retry.
Interfaces e mensagens foram localizadas em inglês, português e espanhol.

Validação concluída com formatação, build Release sem avisos, 1.472 testes .NET,
suíte completa do Worker, matriz de migrações D1 e empacotamento Wrangler em
dry-run. Não houve deploy: a ativação operacional requer aplicar a migration
0012, configurar os novos secrets/bindings documentados e habilitar TOTP no
Identity Platform.
