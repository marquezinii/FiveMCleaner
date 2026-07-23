# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui. O versionamento
segue [Semantic Versioning](https://semver.org/lang/pt-BR/): correções usam
`patch`, melhorias compatíveis usam `minor` e mudanças incompatíveis usam
`major`.

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
