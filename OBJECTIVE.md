# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Substituir, quando o processo ainda estiver recuperável, a apresentação genérica de falhas por uma experiência de erro própria, segura e útil do Ralven.
- **Escopo:** Captura global WPF, exibição fatal e recuperável, detalhes técnicos sanitizados, ações de reinício/fechamento/relato, integração ao diagnóstico existente, modo controlado de validação, testes e localização.
- **Fora de escopo:** Capturar crashes irrecuperáveis do processo, reduzir proteções de crash reporting, alterar o broker, updater ou o comportamento de ações de otimização fora da apresentação de erro.
- **Critérios de conclusão:** Erros de dispatcher e falhas recuperáveis usam UI própria quando disponível; dados técnicos permanecem sanitizados; o fallback é seguro; ações funcionam; textos são localizados; build e testes aplicáveis passam.
- **Resultado entregue:** Janela localizada de falha com divulgação progressiva, cópia sanitizada, reinício seguro e encaminhamento ao formulário de bug para falhas recuperáveis; captura WPF e startup usam essa superfície; logs locais são sanitizados; prévia controlada fica restrita a desenvolvimento; testes, build e validação de localização foram executados.
