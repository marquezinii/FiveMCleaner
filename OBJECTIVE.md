# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Remover os anéis brancos indevidos de toggles e controles relacionados, preservando o foco visível por teclado.
- **Escopo:** Estilos WPF compartilhados e validação focada; sem mudança de comportamento de produto ou textos públicos.
- **Critérios de conclusão:** A interação pelo ponteiro não deixa anel de foco branco; a navegação por teclado mantém indicador claro; build e testes aplicáveis passam.
- **Resultado entregue:** Os controles compartilhados agora usam o foco visual do WPF exclusivo da navegação por teclado, então a ativação pelo ponteiro não deixa anel de foco. Build e 1.570 testes passaram; a página Configurações/Privacidade foi capturada no tema escuro.
