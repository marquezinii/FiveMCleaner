# Objetivo

- **Agente:** Codex
- **Objetivo:** preparar o Ralven para futuras refatorações C#/.NET seguras e semanticamente precisas no Claude Code.
- **Escopo:** configurar SharpLens MCP localmente, orientar o Claude Code e reforçar analisadores e gates de qualidade sem alterar comportamento de produção.
- **Fora do escopo:** refatorar código de produção, mudar comportamento funcional, atualizar versões de produto ou publicar release.
- **Critérios de conclusão:** configuração local do SharpLens conectada à `Ralven.slnx`; instruções de uso seguro atualizadas; análise estática configurada de forma centralizada e sem transformar o passivo em erro; restore, build, testes e formatação validados conforme possível.
- **Resultado entregue:** SharpLens 1.6.3 conectado localmente à solução; instruções do Claude Code e gates de análise estática centralizados. Restore, build Release (zero avisos/erros), 1.432 testes e formatação foram aprovados.
