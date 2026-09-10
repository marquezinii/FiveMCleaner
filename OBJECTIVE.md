# Experiência de diálogos

- **Agente:** Codex.
- **Objetivo:** unificar as janelas secundárias em superfícies modais confortáveis, acessíveis e integradas visualmente ao Ralven.
- **Escopo:** sete janelas WPF, confirmações/avisos do aplicativo, shell compartilhado, teclado, foco, dimensões, rolagem e documentação. Preservar contratos de autenticação, consentimento e operações; seletores do Windows e falha fatal continuam nativos.
- **Critérios de conclusão:** build e suíte Release; renderização dos principais formulários em temas/resoluções distintos; verificações de foco, fechamento e confirmação; PR para integração.
- **Resultado entregue:** sete janelas migradas para `DialogWindow`; confirmações/avisos unificados; opções sem contorno branco, ações fixas, formulários e leitores roláveis, limites por monitor/DPI e foco compartilhado. Build Release sem avisos; 1.440 testes aprovados; probe WPF com 32 combinações aprovadas (claro/escuro, normal/480×520), incluindo cadastro/termos aninhados, Tab/Escape, seleção, rolagem e consentimento. Conversão DPI testada em 100/125/150/200%; movimentação entre monitores físicos e leitor de tela não exercitados. Documentação e roadmap atualizados; entrega por PR sem merge.
