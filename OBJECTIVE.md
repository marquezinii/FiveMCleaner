# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** ampliar o Ralven AI para orientar e acionar capacidades locais já suportadas, sem expor shell, caminhos livres ou privilégios ao modelo.
- **Escopo:** contratos tipados de intenção, fontes/evidências exibidas na conversa, ferramentas locais de leitura e transição explícita para a revisão do plano existente; integração Worker/Responses API para esses contratos.
- **Fora do escopo:** comandos arbitrários, execução automática de mudanças persistentes, novos ajustes de Windows/FiveM, acesso a arquivos, telemetria ampliada, alteração de `main` ou publicação.
- **Critérios de conclusão:** toda intenção da IA é validada localmente e só reutiliza fluxos existentes; alterações continuam passando pela revisão/confirmação transacional; fontes permanecem sanitizadas; testes e build aplicáveis passam.
- **Resultado entregue:** solicitações de ferramentas locais fechadas, fontes
  exibidas e revisão do plano validada; aguardando integração.
