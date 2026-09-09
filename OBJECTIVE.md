# Logo interativa do Ralven AI

- **Agente:** Codex.
- **Objetivo:** animar discretamente a logo principal do Ralven AI por idle e clique.
- **Escopo:** componente WPF reutilizável, PNGs/temas existentes, intervalo configurável, acessibilidade e ciclo de vida; sem mudar navegação, acesso ao AI ou backend.
- **Critérios de conclusão:** repouso entre giros de 10–20 segundos, clique/teclado e cliques repetidos seguros, pausa fora de exibição, build e testes aplicáveis, atalho de desenvolvimento reconstruído.
- **Resultado entregue:** `AnimatedRalvenLogo` integrado ao estado vazio, giro de 720 ms e escala sutil, pausas configuráveis de 10–20 s, fila limitada a uma repetição, clique/teclado, PNGs por tema e localização EN/PT-BR/ES. Animações e timer pausam fora da janela ativa ou da exibição; inscrições são removidas ao descarregar.
- **Validação:** restore e build Release sem avisos/erros; 1.435 testes aprovados, incluindo regressão WPF de 50 cliques, idle, repouso, ocultação e recarga; capturas reais da página em tema escuro (1440×900 DIP) e claro (1100×760 DIP), em modo sintético. Atalho reconstruído e destino confirmado; o cache do espelho exigiu um build `--no-incremental` antes do script padrão.
- **Integração:** pronto para integração; o PR #149 redesenha a mesma página. Preservar o componente no local da logo central durante a combinação. Não houve merge nem release; validação visual usou dados sintéticos, sem backend real.
