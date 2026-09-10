# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Garantir cobertura completa de localização em português, inglês, espanhol e francês e tornar a manutenção dos catálogos verificável e escalável.
- **Escopo:** Auditar textos públicos do aplicativo e componentes auxiliares, corrigir catálogos e consumo de cultura, adicionar francês, criar sincronização/validação versionada e validar os quatro idiomas. Não inclui tradução dinâmica em runtime, publicação ou alteração de versão.
- **Critérios de conclusão:** Todos os catálogos possuem o mesmo conjunto de chaves e placeholders; textos públicos usam a infraestrutura de localização; francês pode ser selecionado sem alterar lógica da aplicação; validações automatizadas falham para chaves ausentes, órfãs, duplicadas ou placeholders divergentes; build, testes e validação visual aplicáveis são executados.
- **Resultado entregue:** Catálogo declarativo com inglês canônico e cobertura equivalente em português, espanhol e francês para aplicativo, atualizador e ações Windows; sincronização, revisão por hash, glossário, pseudo-localização e gates de CI/release adicionados. Foram validadas 2.008 chaves em três conjuntos, build Release sem avisos, 1.446 testes, contrato do instalador e 70 capturas visuais (mais recapturas dos ajustes de expansão no Overview).
