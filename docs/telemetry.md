# Diagnósticos essenciais, dados opcionais e privacidade

Para contrato, troubleshooting e checklist de release da infraestrutura, veja
[Operação da telemetria em produção](telemetry-operations.md).

## Consentimento

Quando uma versão altera a política, a tela de transparência volta a aparecer
e só pode ser fechada por **Continuar**. Sem mudança na política, a escolha
salva não é perguntada de novo.

Antes da confirmação da versão vigente do aviso, nenhum diagnóstico remoto ou
relatório de falha é enviado. Depois da confirmação, os diagnósticos essenciais
allowlisted permanecem ativos para estabilidade, atualizações e resultado
técnico das operações.

A opção única **Relatórios opcionais** fica em **Configurações**, vem habilitada
por padrão em instalações novas e pode ser desligada a qualquer momento. Ela
controla em conjunto a telemetria detalhada de uso e os relatórios sanitizados
de erros e crashes enviados ao Sentry. Um opt-out já salvo por versão anterior
é preservado. Nenhum desses fluxos inclui HWID, MAC, serial, nome do PC, usuário
do Windows ou caminhos locais completos.

## Dados enviados, finalidade, retenção e destinatários

Ao término, falha ou cancelamento de uma otimização, o aplicativo monta um
evento técnico com estes campos (versão 8 do aviso de privacidade —
ver `PrivacyConsentPolicy`):

| Campos | Finalidade | Obrigatório | Retenção | Destinatários |
| --- | --- | --- | --- | --- |
| ID do evento | Correlacionar a execução com seu relatório local e garantir entrega idempotente sem identificar máquina ou usuário. Em otimizações, é o UUID aleatório da transação. | Sim, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Tipo do evento, tempo de execução e versão do app | Distinguir conclusão, falha ou cancelamento; detectar operações anormalmente longas e correlacioná-las à versão. | Sim, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, banco D1 e painel administrativo autenticado com métricas agregadas. |
| Categoria de erro allowlisted (`cancelled`, `timeout`, `access-denied`, `io`, `invalid-data`, `unexpected`) | Classificar falhas sem enviar mensagem, stack trace, arquivo ou caminho. | Sim em falhas, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Código técnico de bug allowlisted (`BugCode`) | Agrupar a causa técnica da falha sem texto livre. | Sim quando a causa é conhecida, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Versão do Windows e arquitetura | Compatibilidade agregada do sistema operacional. | Sim, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Build do Windows | Identificar incompatibilidades específicas de uma build. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Modelo de CPU e GPU; faixa de RAM | Estatísticas agregadas do hardware mais comum. A RAM é arredondada para uma faixa fixa. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Perfil escolhido e amostra de até 30 IDs allowlisted das ações planejadas | Medir uso agregado de perfis e funcionalidades. A contagem de alvos informa o total quando a lista é parcial. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| FiveM detectado; edição do GTA V; contagem de alvos | Verificar instalação sem caminho, edição suportada e escopo da execução. | Sim, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Tipo de disco; faixa de espaço livre | Contextualizar I/O e falta de espaço sem enviar valor exato fora das faixas permitidas. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Timestamp da execução; dias desde a última execução em faixa | Calcular padrões agregados de horário e frequência. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Quantidade de processos FiveM/GTA em faixa | Avaliar se o jogo estava em execução. | Não. Só com **Relatórios opcionais** ativos. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |
| Ambiente (`Development` ou `Production`) | Separar dados de desenvolvimento dos dados da distribuição pública. | Sim, diagnóstico essencial. | Fila local: até 14 dias. D1: não há expiração automática definida no contrato atual. | Worker Cloudflare, D1 e painel administrativo autenticado. |

Os campos essenciais são transmitidos após a confirmação do aviso vigente. Os
demais campos da tabela só são incluídos com **Relatórios opcionais** ativos. A
retenção no D1 ainda não possui prazo automático implementado. Essa ausência é
informada aqui para não sugerir um limite que o backend não aplica.

As únicas categorias de erro possíveis são `cancelled`, `timeout`,
`access-denied`, `io`, `invalid-data` e `unexpected`. Mensagens de exceção,
stack traces, nomes de arquivos e caminhos locais nunca entram nesse contrato.
Modelo de CPU/GPU e faixa de RAM são os mesmos dados já mostrados no
diagnóstico local do app. Os modelos podem identificar o hardware comercial,
mas nunca incluem número de série, MAC ou GUID de hardware. O transporte é o Worker Cloudflare
(`CloudflareTelemetryService.cs`), que transmite todos os campos da tabela
acima. O FormSubmit foi removido por completo do app: não existe mais
código nem configuração que envie telemetria de uso para ele.

## Dados que o aplicativo nunca envia nessa telemetria

- arquivos, imagens, documentos ou seus conteúdos;
- histórico de otimizações, logs locais, relatórios técnicos ou journal;
- nomes de usuário, e-mail, identificadores de máquina (número de série, MAC,
  GUID de hardware), IP como campo do aplicativo, processos ou lista de
  programas instalados, ou configurações do Windows além do que está na
  tabela acima;
- texto livre, mensagens de erro brutas, stack traces ou caminhos.

A fila preserva o UUID aleatório de cada evento — o mesmo UUID da transação nas
otimizações — em todos os retries. Ao
desativar **Relatórios opcionais**, campos opcionais também são removidos dos
eventos que já estavam pendentes antes de qualquer novo envio. O Worker
grava um lote em uma única transação D1; repetir o mesmo lote com UUID do
cliente não cria eventos ou ações adicionais. Enquanto houver instalações
anteriores sem UUID, o Worker gera um UUID apenas para compatibilidade, sem
promessa de idempotência para esse protocolo legado. O código limita os nomes
de evento e categorias a uma allowlist e recusa campos fora desse esquema.
Falhas de rede são isoladas: não interrompem a otimização nem geram nova
telemetria. O evento permanece na fila e é reenviado oportunisticamente na
próxima execução ou inicialização autorizada; não existe loop de retry nem
polling em segundo plano. Rejeições permanentes do Worker são contabilizadas
como falha e removidas para não bloquear a fila.
A fila local (`LocalTelemetryQueue`) persiste eventos pendentes por até 14 dias
antes de descartá-los, para sobreviver a reinícios e períodos offline sem
crescer indefinidamente.

## Destino e metadados de transporte

A telemetria é enviada ao endpoint HTTPS do Worker Cloudflare (rota
`/telemetry`, ver abaixo) — esse é o único transporte, o FormSubmit não é
mais usado para nada relacionado a telemetria de uso. O payload do
Ralven não contém dados pessoais. Como em qualquer conexão HTTPS, a
infraestrutura de rede pode processar metadados de conexão, como endereço
IP, conforme suas próprias políticas; isso não é controlado nem incluído
como campo pelo aplicativo.

Para relatar um problema com descrição e, opcionalmente, e-mail ou trecho de log, use o formulário de bug
separado e opt-in; suas regras estão em [Relatos de bug e privacidade](bug-reports.md).

## Relatório de falhas (Sentry)

Relatórios automáticos de crash fazem parte de **Relatórios opcionais**, que vem
ativado em instalações novas e pode ser desativado a qualquer momento. O SDK do
Sentry só é inicializado depois da confirmação da versão vigente do aviso e
enquanto essa opção permanece ativa. Eles seguem a mesma sanitização e
minimização descritas nesta página.

### Dados enviados com a opção ativada

Quando o aplicativo trava ou encontra uma exceção não tratada, envia ao Sentry:

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo e mensagem sanitizados da exceção | `IOException: could not read %APPDATA%\...` | identificar a causa técnica |
| Stack trace sanitizado | caminhos do usuário substituídos por `%APPDATA%`/`%USERPROFILE%`/etc. | localizar o ponto de falha no código |
| Versão do aplicativo | `1.1.0` | correlacionar com uma versão específica |
| Ambiente | `Development` ou `Production` | nunca mistura erros de desenvolvimento com erros de usuários finais |

O SDK do Sentry só é inicializado após essa autorização, com
`SendDefaultPii=false`, `AutoSessionTracking`/`CaptureFailedRequests`/
`TracesSampleRate` desligados (nenhum dado além do evento de erro em si é
enviado) e um `BeforeSend` obrigatório (`CrashReportSanitizer`) que reaplica
a mesma sanitização de caminhos já usada no relatório técnico
(`ReportSanitizer`) sobre mensagem, stack trace e qualquer dado de usuário
que o SDK tente preencher automaticamente — nome da máquina, IP e
identificador de usuário são sempre sobrescritos/limpos, nunca enviados.

### Configuração centralizada e ambientes

O DSN do Sentry não é um literal espalhado pelo código: fica em
`src/Ralven.App/Config/appsettings.Development.json` e
`appsettings.Production.json` (com `appsettings.json` como base/fallback
seguro, sem DSN). `AppEnvironment.Resolve()` decide qual arquivo usar: a
variável de ambiente `RALVEN_ENVIRONMENT` tem prioridade (é isso que
`scripts/Start-DevelopmentApp.ps1` define como `Development`); sem ela, uma
build Debug resolve para `Development` e uma build Release (a distribuição
pública real) resolve para `Production`. Isso garante que erros do
desenvolvedor rodando localmente nunca se misturam, no Sentry, com erros de
usuários finais rodando a versão instalada — ambos usam o mesmo projeto e
DSN do Sentry, apenas com a tag `Environment` diferente.

### Cloudflare Worker/D1 e painel administrativo

O Worker que recebe a telemetria de uso (não os relatórios de falha, que
vão direto ao Sentry) está **implantado** em
`https://fivemcleaner-telemetry.felipemarquesini10.workers.dev`, com
validação server-side, schema D1 (incluindo uma tabela normalizada de ações
aplicadas, para "função mais usada"), endpoints de estatística agregada
(`/api/stats/*`) e autenticação própria protegendo esses endpoints —
código-fonte e documentação completa em
`infra/cloudflare-worker/README.md`. O cliente .NET aponta
`RemoteServicesOptions.TelemetryEndpoint` para a rota `/telemetry` desse
Worker em ambos os arquivos de configuração por ambiente; o FormSubmit foi
removido do código, não existe mais um caminho alternativo de telemetria.

O Worker também recebe os relatos de bug (rota `/bugs`, ver
[Relatos de bug e privacidade](bug-reports.md)) — somente texto, sem anexo
de captura de tela e sem depender de R2, guardados só no D1.

O painel administrativo privado em `https://dashboard.vemryx.com` consome esses endpoints para
mostrar gráficos agregados — otimizações por dia, versões do Windows/app,
funções mais usadas, hardware mais comum, tempo médio, taxa de sucesso e,
para investigar bugs mais rápido, erros por categoria, ações mais
associadas a falhas, um feed não agregado dos últimos erros e uma aba
**"Bugs reportados"** com os relatos recebidos pela rota `/bugs` (categoria e
código allowlisted, resumo, versão, perfil, ambiente, e-mail opcional e se um trecho de log foi
enviado — sem captura de tela, esse formulário é só texto). Nenhum dado
individual de usuário é exibido nem poderia ser, já que a telemetria nunca
carrega um identificador de máquina; o painel deixa isso explícito em vez
de fingir uma contagem de "usuários únicos" que os dados não permitem
calcular corretamente.

A autenticação do painel foi uma decisão explícita do usuário: sem domínio
próprio, sem Cloudflare Access, sem OAuth Google/GitHub — uma senha de
administrador (hash PBKDF2, nunca em texto puro, gerado localmente e
guardado só como Secret do Worker), proteção contra força bruta e sessões
revogáveis no lado do servidor, desenhada para poder ser trocada por outro
provedor no futuro sem reescrever o resto do Worker. Detalhes completos em
`infra/cloudflare-worker/README.md`.

## Dados locais do Ultra

[Rotinas, observações e medições Ultra](ultra.md) ficam somente no perfil local do
Windows, em `Personal/workspace.json`, sem sincronização remota. O acompanhamento
é opt-in e para com o aplicativo fechado ou sem Pro vigente. Nomes de tarefas,
assinatura de hardware e valores das medições não ampliam os payloads de
telemetria, crash reporting ou relatos. A validação do entitlement usa apenas o
endpoint autenticado já existente; não envia os dados do workspace.
