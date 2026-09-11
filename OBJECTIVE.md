# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Corrigir o contraste dos botões do aplicativo nos temas claro e escuro, inclusive no estado padrão sem foco ou hover.
- **Escopo:** Estilos compartilhados de botões e suas verificações visuais. Não inclui alterações de comportamento, textos ou navegação.
- **Critérios de conclusão:** Botões compartilhados mantêm texto e superfície legíveis nos dois temas, sem regressão nos estados hover, foco e desabilitado; build e testes aplicáveis aprovados.
- **Resultado entregue:** O template compartilhado agora propaga o `Foreground` da variante para o conteúdo, preservando o contraste definido pelos tokens nos estados padrão, hover e pressionado. Build Release sem avisos e 1.570 testes aprovados.
