# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui. O versionamento
segue [Semantic Versioning](https://semver.org/lang/pt-BR/): correções usam
`patch`, melhorias compatíveis usam `minor` e mudanças incompatíveis usam
`major`.

## [1.1.2] - 2026-07-30

### Corrigido

- Corrigida a inicialização silenciosa do instalador de atualização em PCs
  onde a pasta de logs ainda não existe. A criação do log é preparada antes
  de iniciar o setup e, se o log não puder ser criado, não bloqueia a
  atualização verificada.

## [1.1.1] - 2026-07-30

### Corrigido

- Corrigido o contrato de telemetria de produção: o cliente agora envia o
  ambiente exigido pelo Worker e descarta rejeições permanentes em vez de
  manter lotes inválidos em fila.
- Corrigidos o consentimento e a fila local para impedir transmissão após a
  revogação da opção e envios duplicados por flushes concorrentes.
- Corrigido o filtro de data final do dashboard e dos relatos de bug, que
  agora inclui integralmente o dia selecionado.

### Melhorado

- Relatos de bug passam pelo Worker e D1 do FiveMCleaner e ficam disponíveis
  no painel administrativo autenticado; o FormSubmit não é mais usado.
- Atualizadas as instruções de operação e a documentação de segurança para
  refletir o fluxo de telemetria e relato de bugs validado em produção.

## [1.1.0] - 2026-07-27

### Adicionado

- Novo consentimento de privacidade versionado, telemetria técnica opcional
  via Worker Cloudflare e relato de bugs em texto com rota própria, validação
  no servidor e painel administrativo protegido.
- Atualização estável de um clique: após a confirmação, o instalador já
  verificado é executado silenciosamente, preserva a instalação e reabre o
  FiveMCleaner atualizado.
- Diagnósticos e ações adicionais para HAGS, Fullscreen Optimizations,
  G-SYNC/FreeSync, drivers, GPUs híbridas, bateria, PCIe ASPM e mouse polling;
  idioma Espanhol incluído na interface.

### Corrigido

- O broker administrativo não desfaz mais ações comuns já concluídas quando
  uma etapa elevada falha; reforçadas leituras de processo, escritas atômicas,
  launches restritos e a fila local de telemetria.
- Corrigidos instância única, detalhes de atualização, notificações de bandeja
  e campos de telemetria para manter os dados enviados válidos e limitados.

### Melhorado

- Barra inferior do plano, dica de privilégio administrativo e telas de
  consentimento ficaram mais claras, sem ampliar permissões do aplicativo.
- Documentação de segurança, privacidade, arquitetura, atualização e catálogo
  foi revisada para refletir os limites e o comportamento efetivamente entregue.
- Pipelines de CI, GitHub Pages e release passaram a usar as revisões atuais
  das ações oficiais do GitHub, mantendo checkout, setup, Pages e atestação
  de proveniência atualizados.

## [1.0.3] - 2026-07-25

### Corrigido

- Corrigidos fluxos transacionais de cancelamento e a persistência do journal
  para que uma execução interrompida não fique presa em estado intermediário.
- Corrigidos gates independentes para preferências gráficas de FiveM e GTA V,
  evitando que um ajuste planejasse indevidamente o outro.
- Corrigidos o diagnóstico de timeout do broker e a atualização do ledger de
  etapas administrativas durante a otimização.

### Melhorado

- Adicionada confirmação temática antes de cancelar ou fechar o aplicativo
  durante uma otimização; o fechamento confirmado aguarda o cancelamento seguro
  da etapa atual.
- O selo **Recomendado** agora segue o diagnóstico real do computador, e o
  plano atual ficou mais limpo ao ocultar metadados internos que poluíam a lista.
- Incluída notificação nativa do Windows quando uma atualização estável é
  encontrada.

### Atualizado

- Ampliados os diagnósticos e as opções opt-in para FiveM/GTA V Legacy,
  incluindo cache, instalação, gráficos, janela/VSync, commandline standalone,
  benchmark oficial do GTA V e comparação local antes/depois.
- Atualizadas as configurações, documentação de segurança, telemetria e a
  licença source-available do projeto para refletir o comportamento atual.

## [1.0.2] - 2026-07-23

### Corrigido

- Corrigido o fechamento inesperado na abertura causado pelo binding da versão
  no painel lateral.
- Corrigido o contraste do número da versão no tema escuro.
- Corrigido o enquadramento da janela maximizada para respeitar a área útil do
  monitor, sem faixas vazias nem rodapé oculto.

### Melhorado

- Refinados o selo de versão, o card de proteção e os seletores de idioma e
  aparência para melhorar legibilidade, alinhamento e consistência visual.
- O rodapé com relato de bug e copyright permanece acessível com a janela
  maximizada.

### Atualizado

- A página pública de download agora mostra a seção **Última versão pública**
  com mudanças verificáveis e link para o histórico completo.

## [1.0.1] - 2026-07-23

### Público

- Corrigida a proporção da arte lateral e o idioma inicial do instalador.
- Adicionados limites de tempo seguros para fases administrativas, evitando que uma etapa fique travada indefinidamente.
- Atualizada a publicação pública com landing no GitHub Pages e download direto do instalador.

## [1.0.0] - 2026-07-23

### Público

- Marco da primeira versão pública estável, mantendo toda a evolução técnica
  entregue antes desta numeração.
- Landing page própria para download, com visual do FiveMCleaner e acesso ao
  instalador oficial pelo GitHub Releases.

### Alterado

- Diagnóstico visual de FiveM Legacy e GTA V Legacy agora apresenta estados
  explícitos de detectado/não detectado; a identificação distingue corretamente
  Windows 11 de builds internos `10.0`.
- A interface recebeu modos com indicadores de intensidade, hardware mais claro
  e uma visão geral mais limpa.

### Política de versão

- As releases estáveis públicas avançam em sequência controlada: `1.0.0` até
  `1.0.99`, depois `1.1.0`; o mesmo padrão vale para cada minor seguinte. O
  workflow valida a próxima versão permitida antes de gerar uma release.

## [0.2.0] - 2026-07-22

### Adicionado

- Instalador `win-x64` autocontido com runtime .NET incluído, idiomas pt-BR e
  inglês, tema moderno, atalhos opcionais e atualização no mesmo diretório.
- Atualizador opt-in via GitHub Releases: valida versão estável, origem HTTPS,
  tamanho e SHA-256 antes de oferecer o instalador; a pessoa pode abrir as
  notas da release antes de baixar.
- Escolha explícita na desinstalação para preservar ou remover dados locais;
  instalações silenciosas preservam esses dados por padrão.
- Workflow manual de release com build, testes, smoke de instalação/upgrade/
  desinstalação, checksums, manifesto e atestação de proveniência.

### Alterado

- Progresso, relatório e apresentação dos perfis passaram a registrar o
  resultado de cada ação de maneira isolada e reversível.
- A interface passou a incluir preferências persistentes, tema, idioma,
  hardware detalhado, bandeja e prontidão local para criadores.

### Segurança

- O instalador não baixa runtimes nem executa PowerShell, CMD ou conteúdo
  remoto. O app não executa um pacote de atualização até a confirmação da
  pessoa e a validação do SHA-256.

## [0.1.0] - 2026-07-18

### Adicionado

- Fundação do diagnóstico, planos de otimização reversíveis, broker elevado
  restrito e documentação de segurança para FiveM Legacy.
