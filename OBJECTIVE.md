# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** suavizar a hierarquia visual da tela Visão Geral com um recorte pequeno, sóbrio e coerente nos temas claro e escuro.
- **Escopo:** ajustar somente o Overview e, se indispensável, estilos de superfície que ele já usa; não alterar cabeçalho/conta, autenticação, navegação, comportamento funcional ou textos.
- **Critérios de conclusão:** reduzir caixas e divisórias duras nos pontos de maior impacto, reutilizar tokens existentes, preservar responsividade/acessibilidade e validar build, testes e renderização em tema claro e escuro quando o ambiente permitir.
- **Resultado entregue:** o diagnóstico principal usa a superfície hero existente com raio amplo e sem contorno; os três KPIs são agrupados por espaço, sem divisórias; o monitor ao vivo fica aberto sobre o fundo e preserva o gráfico como poço funcional. O XAML foi inspecionado em tema escuro e claro, maximizado e em 1600×1000. Foram aprovados 78 testes focados de localização/tokens, o build Release da solução (0 avisos) e a suíte completa (1.434 testes).
