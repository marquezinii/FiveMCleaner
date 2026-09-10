# Objetivo da tarefa

- **Agente**: Codex
- **Objetivo**: Fazer o conteúdo textual do Ralven AI usar Geist Sans como fonte principal, com fallback confiável e sem alterar a identidade tipográfica do restante do aplicativo.
- **Escopo**: Recurso de fonte incorporado, token tipográfico específico da página do Ralven AI e aplicação consistente aos textos e controles dessa conversa. Fora do escopo: novo renderer Markdown, redesign visual, alteração da fonte global do Ralven ou mudança funcional nas respostas.
- **Critérios de conclusão**: respostas, listas, títulos, citações, blocos e elementos textuais renderizados na página do Ralven AI herdam Geist Sans; fontes monoespaçadas existentes continuam preservadas; build/testes aplicáveis passam; a tela é inspecionada visualmente sem regressões evidentes de layout, quebra, espaçamento ou truncamento.
