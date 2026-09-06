# Objetivo da tarefa

## Identificação

- Agente: Codex
- Branch: `feat/general-optimizer-ux`
- PR de destino: `dev/proxima-versao`

## Objetivo

Simplificar e fortalecer a experiência da aba Otimizador Geral, preservando a
linguagem visual de cards e mantendo detalhes técnicos acessíveis sob demanda.

## Escopo

- Inclui: hierarquia visual, microcopy, perfis, plano, progresso, resultado,
  acessibilidade e localização do Otimizador Geral.
- Exclui: alterações nas ações de otimização, no restante do produto e na
  lógica de sistema. Componentes compartilhados foram ajustados somente quando
  necessário para preservar a experiência e os contratos existentes.

## Critérios de conclusão

- Usuários comuns conseguem escolher um perfil e entender o impacto antes de
  executar, sem precisar ler detalhes do Windows.
- Riscos materiais, necessidade de confirmação e reinicialização continuam
  visíveis quando relevantes.
- Risco, rollback, verificação e diagnóstico técnico continuam acessíveis por
  disclosure.
- A interface renderiza corretamente nos temas claro e escuro, inclusive em
  janela menor, e a validação automatizada aplicável passa.

## Resultado entregue

O Otimizador Geral passou a priorizar benefício e impacto prático, com detalhes
técnicos, etapas concluídas e relatório completo recolhidos por padrão.
Referências desnecessárias a FiveM/GTA foram removidas do contexto geral.
Build Release, 1.372 testes, verificação de formato, diff e capturas reais nos
temas claro e escuro foram aprovados.
