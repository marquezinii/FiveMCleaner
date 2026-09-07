# Objetivo da tarefa

## Identificação

- Agente: Codex
- Branch: `feat/ralven-ai`
- PR de destino: `dev/proxima-versao`

## Objetivo

Adicionar uma primeira versão segura do Ralven AI para usuários Pro, usando o
diagnóstico e o catálogo existentes para oferecer orientação contextual sem dar
ao modelo acesso direto ao Windows ou a comandos arbitrários.

## Escopo

- Inclui: superfície localizada no app, contexto técnico sanitizado e
  allowlisted, chamada autenticada ao Worker, validação server-side do acesso
  Pro, limites de requisição/uso e recomendações restritas a IDs de ações que o
  Ralven já conhece.
- Inclui: confirmação explícita pelo usuário antes de qualquer fluxo de
  otimização e tratamento seguro de indisponibilidade, respostas inválidas e
  limites de uso.
- Exclui: novas otimizações, shell/scripts gerados por IA, execução direta pelo
  modelo, coleta contínua de telemetria, roteamento multimodelo, publicação ou
  alteração da cobrança pública.

## Critérios de conclusão

- Uma conta Pro autenticada consegue solicitar orientação contextual por uma
  rota protegida do Worker; conta Free, entitlement indisponível e payload
  inválido falham fechados antes de chamar o provedor.
- Somente dados técnicos explicitamente allowlisted podem sair do cliente, e a
  credencial do provedor permanece exclusivamente no Worker.
- Recomendações desconhecidas ou incompatíveis são descartadas e nenhuma ação é
  aplicada sem o fluxo de confirmação já existente no Ralven.
- UI, contratos, Worker e testes aplicáveis preservam segurança, privacidade,
  localização e compatibilidade existentes.

## Resultado entregue

- Página localizada do Ralven AI para contas Pro, com contexto derivado do
  diagnóstico, conversa apenas em memória e revisão explícita do perfil no
  planejador transacional existente.
- Rota `POST /ai/message` autenticada e fail-closed para entitlement, rate
  limit e orçamento, com credencial somente no Worker, saída estruturada e
  ledger D1 sem conteúdo da conversa.
- Testes do serviço desktop, contrato localizado, Worker, migrações e controle
  de orçamento, além de documentação de arquitetura, segurança, privacidade e
  ativação operacional.
- Validação concluída com build Release sem avisos, 1.376 testes .NET, 245
  testes do Worker, verificação de segurança, formatação e auditorias de
  dependências. O deploy, a migration remota, o secret e o smoke test real do
  provedor permanecem deliberadamente fora desta tarefa.
- Integração: renumerada a migration D1 de `0009` para `0010` por colisão com
  a migration de billing (PR #128, já integrada); corrigido content-type da
  chamada `POST /ai/message` que fazia toda requisição real falhar com 415, e
  chamadas falhas ao provedor deixaram de consumir permanentemente a reserva
  mensal de orçamento.
