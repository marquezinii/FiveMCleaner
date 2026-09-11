# Objetivo

- **Agente:** Codex
- **Objetivo:** Tornar a detecção do FiveM resiliente a instalações válidas em locais e estados suportados, sem ampliar o escopo de acesso nem aceitar falsos positivos.
- **Escopo:** Centralizar descoberta, validação, cache e fallback manual usados pela área Jogos/FiveM, com testes de regressão e textos localizados necessários.
- **Fora de escopo:** Alterar arquivos do FiveM/GTA V, executar reparos, instalar software ou suportar GTAV Enhanced.
- **Critérios de conclusão:** Fontes de descoberta são combinadas por camadas, candidatos são validados e classificados de modo determinístico, consumidores usam o resultado central, e as validações aplicáveis passam.
- **Resultado entregue:** Localizador central em camadas, cache invalidável, seleção manual validada, consumidor de runtime unificado e 10 testes focados; build Release e suíte .NET aprovadas. A aprovação humana contextual das novas traduções ainda é exigida pelo verificador de localização antes do commit/PR.
