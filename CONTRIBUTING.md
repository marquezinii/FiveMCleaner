# Contribuindo com o FiveMCleaner

Obrigado por ajudar a construir uma ferramenta transparente e segura para FiveM Legacy. Mudanças pequenas, bem justificadas e reversíveis são preferíveis a listas extensas de “tweaks”.

## Antes de começar

- Leia [docs/research.md](docs/research.md), [docs/safety.md](docs/safety.md) e [docs/architecture.md](docs/architecture.md).
- Procure uma issue existente antes de abrir outra.
- Para mudanças grandes ou novas ações de sistema, proponha primeiro o comportamento, evidências e rollback em uma issue.
- Vulnerabilidades seguem [SECURITY.md](SECURITY.md), nunca uma issue pública.
- O escopo atual é **FiveM para GTAV Legacy**. Alterações para Enhanced devem manter o bloqueio seguro, salvo decisão arquitetural posterior documentada.

## Ambiente local

Requisitos:

- Windows 10 ou 11 x64;
- .NET SDK indicado em `global.json`;
- Git com suporte a caminhos longos habilitado, se necessário;
- FiveM Legacy somente para testes de integração opcionais.

```powershell
git clone <url-do-fork>
Set-Location FiveMCleaner
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
```

Não execute testes destrutivos na instalação real do FiveM. Use diretórios temporários e doubles para registro, processos e sistema de arquivos. Testes que realmente exigirem elevação devem ser isolados, explícitos e nunca fazer parte do fluxo padrão.

## Fluxo de contribuição

1. Crie uma branch curta, por exemplo `feat/preview-cache` ou `fix/rollback-xml`.
2. Mantenha cada commit focado em uma decisão.
3. Atualize testes e documentação junto com o comportamento.
4. Rode build e testes em `Release`.
5. Abra um pull request preenchendo todo o checklist.

Não inclua binários gerados, caches, dumps, logs pessoais ou credenciais no commit.

## Critério para uma nova ação

Toda ação que lê ou altera o sistema deve declarar:

- identificador estável e versão;
- edição suportada e pré-condições;
- fonte primária ou benchmark reproduzível;
- estado detectado e mudança exata;
- efeito esperado em linguagem não garantida;
- nível de risco e necessidade de elevação/reinício;
- comportamento idempotente;
- snapshot, validação e rollback;
- caminhos permitidos e dados protegidos;
- testes para sucesso, falha parcial, cancelamento e restauração.

Uma dica de fórum, vídeo ou pacote de “FPS boost” não é evidência suficiente. Separe fatos observados de inferências de engenharia e evite números de desempenho sem metodologia, amostra e hardware documentados.

## Limites de segurança

Contribuições não podem:

- executar PowerShell, CMD ou payload arbitrário em modo elevado;
- desativar Defender, firewall, UAC, Windows Update ou serviços essenciais;
- criar exclusões de antivírus ou sugerir que o usuário desative sua proteção;
- injetar código, alterar memória ou patchar binários do FiveM, GTA ou Windows;
- escrever em `commandline.txt` como otimização do FiveM;
- sobrescrever perfis NVIDIA ou apagar shader cache por padrão;
- apagar `game-storage`, autenticação, configurações ou plugins;
- tratar cache como ganho universal de FPS;
- aplicar caminhos e regras do Legacy ao GTAV Enhanced.

Veja a matriz completa em [docs/safety.md](docs/safety.md).

## Estilo e arquitetura

- Preserve a separação entre interface, política, integrações Windows e broker elevado.
- O domínio não deve depender diretamente de WPF, registro ou sistema de arquivos.
- Prefira contratos tipados, `CancellationToken` e resultados estruturados.
- Logs devem ser úteis sem conter dados pessoais por padrão.
- Textos da interface são em pt-BR; identificadores de código permanecem em inglês.
- Respeite `.editorconfig` e as configurações compartilhadas em `Directory.Build.props`.

## Pull requests

O pull request deve explicar o problema e a decisão, não apenas listar arquivos. Inclua evidências, riscos, capturas para alterações visuais e o resultado exato dos testes. Um mantenedor pode solicitar benchmark ou validação em máquina descartável antes de aceitar uma nova ação do sistema.

Ao contribuir, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md) e
aceita os termos de contribuições da [Licença Source-Available do
FiveMCleaner](LICENSE), inclusive a licença necessária para que o mantenedor
incorpore sua contribuição ao projeto.
