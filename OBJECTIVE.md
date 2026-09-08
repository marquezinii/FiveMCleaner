# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** validar e endurecer os fluxos da aba Aplicativos sem ampliar o escopo do produto.
- **Escopo:** descoberta, inventário, inicialização somente leitura, pesquisa, instalação, atualização, desinstalação e preferências de atualizações ignoradas por WinGet.
- **Fora do escopo:** outras abas, otimizações, broker, updater, instalador, fontes fora de `winget`/`msstore` e mudanças de release.
- **Critérios de conclusão:** fluxos rastreados de UI a serviço Windows; entradas e operações validadas; correções mínimas cobertas por testes; build e testes relevantes executados.
- **Resultado entregue:** o serviço WinGet agora recusa executáveis fora do alias oficial do usuário; teste de regressão adicionado e a simulação do caminho oficial foi tornada independente do perfil local.
