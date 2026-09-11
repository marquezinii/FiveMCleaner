# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Compactar pontualmente o painel da bandeja e preservar um indicador de foco visível apenas para navegação por teclado.
- **Escopo:** `TrayMenu.xaml`, a composição visual do cabeçalho do menu e a cobertura de contrato do painel. Não alterar navegação, comandos, textos, estrutura nem o sistema visual geral.
- **Critérios de conclusão:** o painel usa menos espaço sem perder legibilidade; o anel de foco não aparece após uso do mouse e segue disponível por teclado; build e testes aplicáveis passam.
- **Resultado entregue:** Painel reduzido de forma proporcional (largura mínima, moldura, cabeçalho, linhas e separadores), mantendo os tokens e a composição existentes. O foco agora usa `FocusVisualStyle` nativo do WPF, removendo o trigger que também desenhava o anel após clique do mouse. Build Release e 1.571 testes aprovados; a inspeção visual foi limitada porque a automação desta sessão não expôs a janela WPF.
