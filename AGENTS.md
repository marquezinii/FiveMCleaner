# AGENTS.md — FiveMCleaner

Instruções operacionais para agentes de IA trabalhando neste repositório.

Este arquivo se aplica ao repositório inteiro. Um `AGENTS.md` mais próximo de um
subdiretório pode adicionar regras específicas daquela área, mas nunca pode
enfraquecer os invariantes de segurança definidos aqui ou em `docs/safety.md`.

## 1. Missão

O FiveMCleaner é uma aplicação Windows para diagnóstico, manutenção e otimização
transparente, conservadora e reversível de **FiveM sobre GTAV Legacy**.

O objetivo não é acumular “tweaks”. Uma mudança só pertence ao produto quando
tem escopo conhecido, justificativa técnica, efeito explicável, validação,
segurança para os dados do usuário e rollback quando aplicável.

Prioridade global:

```text
corretude
> segurança
> reversibilidade
> integridade dos dados
> transparência
> compatibilidade
> UX
> desempenho
> velocidade de implementação
```

Nunca troque segurança ou corretude por uma otimização mais agressiva, um fluxo
mais conveniente ou um “fix” rápido.

## 2. Fontes de verdade

Antes de editar, use estas fontes:

- `AI_RULES.md`: Git, worktrees, branches, commits, integração e release.
- `PROJECT_STATE.md`: estado oficial já integrado.
- `docs/architecture.md`: responsabilidades e fronteiras.
- `docs/safety.md`: invariantes e ações proibidas.
- `docs/research.md`: evidências para decisões de otimização.
- `docs/telemetry.md`: telemetria, privacidade e crash reporting.
- `docs/bug-reports.md`: relatos de bug.
- `docs/release-preview.md`: preview, build e release.
- `README.md`: comportamento prometido publicamente.
- `CHANGELOG.md`: histórico público.

Código e testes são evidência primária do comportamento existente. Se
documentação, código e testes divergirem, investigue antes de escolher um lado.
Não “corrija” a divergência por palpite.

## 3. Antes de qualquer alteração

Siga integralmente `AI_RULES.md`. Em especial:

1. localize a raiz real do repositório;
2. inspecione branch, `git status`, histórico e worktrees;
3. leia o contexto obrigatório definido em `AI_RULES.md`;
4. leia os documentos diretamente relacionados à área afetada;
5. inspecione a implementação atual e seus testes;
6. identifique trabalho concorrente antes de tocar em arquivos disputados;
7. execute a tarefa no isolamento Git exigido pelo projeto.

Não pergunte ao usuário algo que o repositório pode responder. Localize,
inspecione e deduza autonomamente decisões mecânicas.

Quando houver dúvida entre uma abordagem agressiva e uma conservadora, escolha a
conservadora.

## 4. Arquitetura

Preserve as responsabilidades atuais:

| Área | Responsabilidade |
| --- | --- |
| `src/FiveMCleaner.App` | WPF, navegação, apresentação, interação e serviços de aplicação |
| `src/FiveMCleaner.Contracts` | DTOs, IDs, estados, erros e contratos compartilhados |
| `src/FiveMCleaner.Core` | casos de uso, políticas, perfis, planejamento, transação e rollback |
| `src/FiveMCleaner.Windows` | descoberta e integrações específicas de Windows/FiveM |
| `src/FiveMCleaner.Broker` | operações administrativas tipadas e allowlisted |
| `src/FiveMCleaner.Launcher` | inicialização e supervisão de saúde pós-update |
| `src/FiveMCleaner.Updater` | staging, ativação e rollback de atualização |
| `src/FiveMCleaner.UpdateRuntime` | primitivas compartilhadas do updater |
| `src/FiveMCleaner.ReleaseTool` | geração e validação de artefatos de release |
| `tests/FiveMCleaner.Tests` | testes de contratos, políticas, segurança e regressões |
| `infra/cloudflare-worker` | backend remoto suportado |
| `infra/dashboard` | painel privado |
| `installer` | instalador Windows |
| `scripts` | automação de desenvolvimento/build/release |
| `website` | site público |
| `docs` | documentação técnica e operacional |

Fronteiras essenciais:

- `Core` não conhece WPF.
- `Contracts` não conhece WPF nem implementação Windows.
- `Windows` descobre fatos e executa integrações; não decide produto.
- `Broker` não contém navegação, telemetria ou lógica ampla de produto.
- a UI não escreve diretamente em registro, configurações do FiveM ou recursos
  administrativos quando existe camada apropriada.
- operações privilegiadas atravessam contratos tipados, nunca comandos livres.

Evite dependências circulares e não mova lógica entre camadas apenas para
“facilitar” uma implementação.

## 5. Invariantes do produto

### FiveM Legacy somente

- O fluxo atual suporta FiveM sobre GTAV Legacy.
- GTAV Enhanced deve resultar em bloqueio seguro.
- Nunca tente Enhanced como Legacy por fallback.
- Suporte futuro a Enhanced deve nascer como adaptador separado, com pesquisa,
  política e testes próprios.

### Diagnosticar antes de alterar

Nunca suponha:

- instalação;
- edição do jogo;
- existência ou formato de arquivo;
- estado de registro;
- hardware;
- processo;
- privilégio;
- versão/suporte do Windows.

Detecte fatos primeiro. Políticas do produto decidem depois.

### Transparência e honestidade

O usuário deve conseguir saber o que será alterado, por quê, risco, efeito
esperado, privilégio, reinício e rollback.

Nunca prometa ganho universal de FPS, ping, stutter ou “zero lag”. Descreva
efeito esperado e limitações.

### Reversibilidade

Alterações persistentes devem, quando aplicável:

1. capturar estado anterior suficiente;
2. aplicar;
3. verificar pós-condição;
4. registrar resultado;
5. restaurar somente o que a própria ação alterou quando necessário.

Rollback faz parte da funcionalidade, não é acabamento posterior.

### Privilégio mínimo

O app principal não roda permanentemente elevado. Administração passa pelo
broker e deve ser mínima, tipada, allowlisted e revalidada do lado privilegiado.

## 6. Segurança: proibições

`docs/safety.md` é obrigatório para qualquer mudança que toque Windows, FiveM,
GTA V, limpeza, processos, arquivos, registro ou privilégio.

Nunca implementar como “otimização”:

- desativar Defender, firewall, SmartScreen, UAC ou antivírus;
- criar exclusões automáticas de antivírus;
- debloat genérico, remoção em massa de AppX ou desativação indiscriminada de
  serviços;
- prioridade `Realtime`, afinidade fixa ou desativação de SMT/Hyper-Threading;
- injeção, leitura/escrita de memória ou modificação de binários;
- bypass de anti-cheat, pure mode ou integridade;
- PowerShell/CMD arbitrário via broker;
- scripts remotos;
- download e execução de código adicional em runtime;
- ações Legacy em GTAV Enhanced;
- apagar dados protegidos chamando-os de “cache”.

Exceções já existentes em `docs/safety.md` só podem continuar existindo sob as
condições exatas de opt-in, detecção, escopo e reversibilidade documentadas.

## 7. Paths, arquivos e processos

Toda operação sensível deve:

- canonicalizar o alvo;
- validar que ele permanece na raiz permitida;
- considerar links/reparse points;
- impedir path traversal;
- validar novamente antes da escrita quando necessário;
- não seguir diretórios para fora do root esperado.

Proteja especialmente dados de autenticação, entitlement, configurações,
plugins, `game-storage`, `nui-storage`, `ipfs`, `CitizenFX.ini` e a instalação
original do GTA V.

Quando a identidade de processo importa, não confie apenas no nome. Use a imagem
executável e sua relação com a instalação detectada.

Ao editar XML/configuração:

- preserve nós e atributos desconhecidos;
- altere somente chaves suportadas;
- valide antes e depois;
- preserve formato quando o comportamento atual exigir;
- use backup/rollback;
- não substitua arquivo inválido por template genérico silenciosamente.

## 8. Ações de otimização

Uma nova ação deve ter, conforme aplicável:

- ID estável;
- evidência/justificativa;
- detecção do estado atual;
- pré-condições explícitas;
- escopo de leitura/escrita;
- estado desejado;
- risco, privilégio e criticidade;
- integração com preview/plano;
- aplicação idempotente;
- verificação;
- rollback;
- resultado semântico correto;
- progresso por etapas reais;
- documentação;
- testes de sucesso, falha e regressão.

Não coloque uma ação em `Leve`, `Médio` ou `Agressivo` antes de provar que ela é
segura isoladamente.

Perfis são composições de ações, não licença para ampliar escopo. Nem
`Agressivo` autoriza comportamento irreversível ou inseguro.

Cache é manutenção/reparo e não deve entrar implicitamente nos perfis padrão.

## 9. Execução transacional

Preserve a semântica existente:

- falha isolada reverte somente a ação que falhou;
- pré-requisito não atendido produz `Skipped`, não execução cega;
- falha crítica impede ações subsequentes inseguras;
- `NotRun`, `Skipped`, `Failed`, `RolledBack` e demais outcomes não são
  intercambiáveis;
- sucesso parcial não pode ser reportado como sucesso total;
- falha/cancelamento da fase elevada não deve desfazer ações independentes de
  usuário padrão já confirmadas, salvo contrato explicitamente diferente;
- cancelamento só ocorre em pontos seguros, nunca no meio de escrita crítica.

Não “simplifique” resultados semânticos para `bool` se isso apagar informação
necessária para segurança, UI ou rollback.

## 10. Broker

`FiveMCleaner.Broker` é código de alta criticidade.

O broker:

- não é shell;
- não aceita script livre;
- não recebe comando arbitrário;
- não recebe “execute isto neste path” de forma genérica;
- não ganha acesso de rede por conveniência;
- não conhece telemetria;
- não conhece UI;
- valida inputs do lado privilegiado;
- aceita somente operações suportadas explicitamente.

Nova operação privilegiada exige contrato específico, validação, escopo mínimo,
verificação de resultado e testes de inputs inválidos/tentativas de ampliar
escopo.

Nunca corrija uma limitação do broker transformando contrato tipado em comando
genérico.

## 11. Updater e cadeia de release

Trate `Launcher`, `Updater`, `UpdateRuntime` e `ReleaseTool` como superfície de
segurança.

Nunca enfraqueça para “fazer atualizar”:

- assinatura;
- origem;
- versão;
- tamanho;
- SHA-256;
- staging;
- ativação atômica;
- health-check;
- rollback.

Se uma validação rejeitou um pacote, descubra por que pacote/metadata está
incorreto. Não transforme falha de validação em warning silencioso.

Release somente no fluxo explicitamente acionado pelo usuário e definido em
`AI_RULES.md`.

Tarefa comum não:

- incrementa versão;
- cria tag;
- publica `main`;
- cria GitHub Release;
- inventa changelog público.

## 12. C# e qualidade interna

Use o SDK e configurações definidos pelo repositório (`global.json`,
`Directory.Build.props`, `.editorconfig`).

Regras:

- preserve nullable corretamente; não espalhe `!` sem prova;
- prefira tipos fortes a strings como protocolo improvisado;
- mantenha métodos/classes coesos;
- não crie “God objects”;
- não crie abstração sem uso real;
- prefira composição a duplicação;
- propague `CancellationToken` em operações longas;
- não bloqueie async com `.Result`/`.Wait()`;
- não faça I/O pesado na thread de UI;
- não esconda falha relevante em `catch { }`;
- preserve a semântica das exceções;
- não introduza warnings novos sem justificativa;
- não reformate arquivos não relacionados;
- não atualize dependências sem relação com a tarefa.

Comentários devem explicar principalmente **por que** algo existe ou qual
invariante protege, não narrar o código linha a linha.

## 13. WPF, UI e localização

Mudanças visuais devem reutilizar tokens, styles, resources, controls e padrões
existentes antes de criar variantes.

Preserve:

- tema do sistema/claro/escuro;
- estados de interação;
- foco e navegação;
- responsividade;
- progresso real;
- cancelamento seguro;
- separação entre View e operações de sistema.

### Textos públicos

Não introduza texto localizado hardcoded.

Ao adicionar/alterar texto público, mantenha a chave correspondente em:

- `src/FiveMCleaner.App/Resources/Strings.resx`
- `src/FiveMCleaner.App/Resources/Strings.pt-BR.resx`
- `src/FiveMCleaner.App/Resources/Strings.es.resx`

Use o mecanismo de localização existente no XAML/código.

Isso inclui `Text`, `Content`, tooltips, mensagens, placeholders, diálogos e
labels. Preserve placeholders e formatação entre os idiomas.

Não use `Task.Delay` para fingir progresso. Progresso deve refletir trabalho
real.

## 14. Autenticação, configuração e secrets

Preserve a arquitetura de autenticação existente.

Nunca:

- persista senha;
- persista ID token em disco;
- registre tokens em log;
- confie em UID/e-mail recebido do cliente quando o backend possui identidade
  autenticada;
- hardcode chave privada, token, senha, DSN secreto ou credencial.

Refresh token persistido deve continuar protegido pelo mecanismo seguro
existente.

Use a configuração por ambiente já adotada pelo projeto. Nunca commite secrets,
dumps sensíveis ou arquivos locais de autenticação.

## 15. Telemetria, crash reporting e relatos

Privacidade é requisito funcional.

Não amplie coleta silenciosamente.

Novo dado remoto exige finalidade, contrato, sanitização, consentimento/política
compatível, documentação e teste.

Evite payload livre quando existe schema allowlisted.

Nunca envie desnecessariamente:

- senha/token/cookie;
- conteúdo de arquivos pessoais;
- credenciais;
- clipboard;
- paths pessoais completos;
- identificadores fora da política vigente.

Telemetria/crash/bug report devem passar pelos sanitizadores apropriados.

Falha de telemetria não deve quebrar a otimização principal.

## 16. Worker/backend

Ao alterar `infra/cloudflare-worker`:

- trate todo input do cliente como não confiável;
- valide autenticação server-side;
- limite formato e tamanho de payload;
- preserve CORS/rate limiting/hardening vigentes;
- sanitize dados e exports;
- não exponha detalhes sensíveis em erros;
- não confie em identidade enviada no body;
- preserve compatibilidade com clientes ainda em uso quando possível;
- adicione testes/migrations quando aplicável.

## 17. Testes

Mudança sem validação é incompleta.

Para bug:

```text
caracterizar/reproduzir
→ localizar causa
→ criar regressão quando viável
→ corrigir na camada correta
→ validar efeitos colaterais
→ executar suíte aplicável
```

Para feature, teste comportamento nominal, limites, falhas, inputs inválidos,
cancelamento, rollback e invariantes de segurança conforme o caso.

Testes unitários não devem depender de FiveM real, privilégio administrativo
real, estado específico da máquina ou rede externa. Use interfaces/doubles.

Comandos base:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
```

Execução local do app:

```powershell
dotnet run --project src/FiveMCleaner.App/FiveMCleaner.App.csproj
```

Para Worker/site/dashboard, inspecione os scripts reais do respectivo projeto.
Não invente comandos.

Se houver falha preexistente, prove contra a base, não atribua à tarefa sem
evidência e registre a limitação.

Nunca afirme que testes passaram se não foram executados.

## 18. Performance

Priorize:

- não bloquear UI;
- evitar enumeração repetida de diretórios grandes;
- evitar hashing/releitura duplicada;
- streaming para dados grandes quando apropriado;
- cancellation;
- progresso real;
- cache somente com invalidação correta;
- liberar handles/streams/processos corretamente.

Não sacrifique corretude por micro-otimização. Alegações de ganho precisam de
medição ou evidência.

## 19. Dependências

Antes de adicionar pacote:

1. verifique se BCL/dependências atuais resolvem;
2. avalie manutenção, segurança e licença;
3. considere impacto no pacote self-contained;
4. evite dependência para utilidade trivial;
5. atualize somente o necessário.

Não faça atualização geral de dependências em tarefa sem relação.

## 20. Documentação

Atualize documentação quando mudar:

- arquitetura;
- segurança;
- comportamento público;
- otimização;
- rollback;
- autenticação;
- telemetria;
- updater;
- build/release;
- requisitos.

Em tarefas isoladas, respeite a política de `PROJECT_STATE.md` definida em
`AI_RULES.md`. Use `.ai/tasks/<tarefa>.md` quando esse for o fluxo exigido.

## 21. Git e concorrência

`AI_RULES.md` é a autoridade detalhada.

Invariantes:

- `main` é versão pública;
- `dev/proxima-versao` é integração;
- tarefas normais usam `ai/<agente>/<tarefa>`;
- use worktree isolado;
- não altere branch do checkout compartilhado;
- não force push;
- não use reset destrutivo em trabalho de terceiros;
- não descarte alterações desconhecidas;
- não reescreva histórico publicado;
- não faça merge em `dev/proxima-versao` ao concluir tarefa normal;
- não publique `main` fora do fluxo oficial.

Use Conventional Commits conforme `AI_RULES.md`.

Assuma sempre que outros agentes podem estar trabalhando. Mantenha diffs
focados e preserve trabalho alheio.

## 22. Antipadrões

Não introduza:

- tweak aleatório de registro;
- script opaco de otimização;
- shell privilegiada genérica;
- executável externo baixado em runtime;
- segurança do Windows enfraquecida para reduzir falso positivo;
- limpeza ampla em perfil automático;
- texto público hardcoded;
- telemetria escondida;
- credencial em código;
- `Task.Delay` fingindo progresso;
- View escrevendo diretamente no sistema;
- app permanentemente elevado;
- `catch` vazio;
- retry infinito;
- timeout maior como substituto de diagnóstico;
- retorno de sucesso após falha;
- abstração criada só para mover poucas linhas;
- refatoração massiva fora do escopo;
- bump de versão em tarefa comum;
- edição indevida de `PROJECT_STATE.md`;
- alteração de `main` fora de release;
- afirmação de validação que não aconteceu.

## 23. Refatorações

Refatoração deve preservar comportamento observável, salvo quando a tarefa
explicitamente altera funcionalidade.

Antes:

- caracterize comportamento;
- localize testes;
- identifique contratos/IDs/formatos persistidos.

Durante:

- faça mudanças coesas;
- evite renomeações massivas;
- não misture feature, redesign e limpeza sem necessidade.

Depois:

- rode testes;
- verifique XAML, serialization, reflection e contratos;
- atualize arquitetura se a fronteira mudou.

## 24. Segurança em código sensível

Ao tocar autenticação, updater, broker, IPC, paths, downloads, assinatura,
telemetria ou crash reports, revise:

- quem controla cada input;
- fronteiras de privilégio;
- path traversal/reparse points;
- TOCTOU;
- substituição de artefato;
- replay/injeção;
- exposição de segredo;
- falha mascarada como sucesso;
- disponibilidade de rollback;
- validação client-side versus server-side.

Segurança não pode depender apenas da UI esconder uma opção.

## 25. Checklist de conclusão

Antes de encerrar:

- [ ] pedido atendido sem mudanças acidentais;
- [ ] camada arquitetural correta;
- [ ] `docs/safety.md` respeitado;
- [ ] privilégio mínimo preservado;
- [ ] nenhum secret incluído;
- [ ] nenhuma validação de segurança enfraquecida;
- [ ] nenhum novo texto público hardcoded;
- [ ] localização atualizada quando aplicável;
- [ ] regressão/feature testada;
- [ ] build aplicável passa;
- [ ] suíte aplicável passa;
- [ ] documentação afetada atualizada;
- [ ] `.ai/tasks/` atualizado conforme `AI_RULES.md`;
- [ ] diff revisado;
- [ ] commit profissional criado;
- [ ] branch/worktree corretos;
- [ ] atalho `FiveMCleaner - Desenvolvimento` reconstruído conforme
      `AI_RULES.md`, quando o ambiente permitir.

## 26. Relatório final

Ao concluir, informe de forma curta e verificável:

- o que mudou;
- principais áreas/arquivos;
- testes/build e resultado;
- branch;
- commit;
- limitações ou validações não executadas;
- `pronto para integração`, quando aplicável.

Não escreva um relatório enorme repetindo o diff.

Nunca diga que algo foi testado, integrado, publicado, reconstruído ou enviado
se isso não aconteceu de fato.

---

**Regra final:** se uma solução só funciona quebrando corretude, segurança,
reversibilidade, transparência ou integridade dos dados, a solução está errada.
