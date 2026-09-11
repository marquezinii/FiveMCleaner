# Objective

- **Agente:** Codex
- **Objetivo:** Reconstruir o dashboard administrativo do Ralven como centro de comando premium, com arquitetura de informação, estados vazios e interações proporcionais aos dados reais disponíveis.
- **Escopo:** `infra/dashboard`, contratos/agregações administrativas do Worker estritamente necessários, documentação e testes associados.
- **Fora do escopo:** Release do aplicativo, mudança de dados pessoais/telemetria sem contrato, alteração de billing, autenticação ou operações privilegiadas.
- **Critérios de conclusão:** A experiência organiza decisões por domínio operacional, explica dados ausentes, oferece drill-down e filtros úteis, preserva acessibilidade/responsividade e passa pelas validações aplicáveis.
- **Resultado entregue:** Centro de comando monocromático em preto, branco e cinzas, com leitura executiva, fila de prioridades derivada somente de agregados reais, estados vazios explicativos e proteção contra converter agregados ausentes em zero. Validado com as 63 verificações do dashboard e em prévia local com dados e sem dados, nos layouts widescreen e reduzido.
