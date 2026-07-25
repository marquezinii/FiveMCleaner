# Telemetria opcional e privacidade

## Consentimento

A opção **Ajude a melhorar o FiveMCleaner** fica em **Configurações** e vem
habilitada por padrão em instalações novas. O usuário pode desligá-la a
qualquer momento; eventos futuros deixam de ser enviados imediatamente assim
que o toggle é desativado. Configurações existentes, salvas por uma versão
anterior do app com o toggle explicitamente desligado, são preservadas
normalmente — a mudança de padrão vale apenas quando o arquivo de
configuração ainda não define esse valor (instalação nova).

## Dados enviados quando autorizados

Ao término, falha ou cancelamento de uma otimização, o aplicativo envia por
HTTPS somente estes campos de um evento técnico:

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo | `optimization-completed` | distinguir conclusão, falha ou cancelamento |
| Tempo de execução | `18342` ms | identificar operações anormalmente longas |
| Versão | `1.0.3` | correlacionar comportamento com uma versão |
| Categoria de erro | `timeout` | presente apenas em falhas; é uma lista fechada |

As únicas categorias de erro possíveis são `cancelled`, `timeout`,
`access-denied`, `io`, `invalid-data` e `unexpected`. Mensagens de exceção,
stack traces, nomes de arquivos e caminhos locais nunca entram nesse contrato.

## Dados que o aplicativo nunca envia nessa telemetria

- arquivos, imagens, documentos ou seus conteúdos;
- histórico de otimizações, logs locais, relatórios técnicos ou journal;
- nomes de usuário, e-mail, identificadores de máquina, IP como campo do
  aplicativo, hardware, processos ou configurações do Windows;
- texto livre, mensagens de erro brutas, stack traces ou caminhos.

O código limita os nomes de evento e categorias a uma allowlist e recusa
campos fora desse esquema. Falhas de rede são ignoradas: não interrompem a
otimização, não geram nova telemetria e não são reenviadas automaticamente.

## Destino e metadados de transporte

Quando habilitada, a telemetria é enviada ao endpoint HTTPS do
[FormSubmit](https://formsubmit.co/privacy.pdf), o mesmo provedor usado pelo
formulário de bugs. O payload do FiveMCleaner não contém dados pessoais.
Como em qualquer conexão HTTPS, o provedor e a infraestrutura de rede podem
processar metadados de conexão, como endereço IP, conforme suas próprias
políticas; isso não é controlado nem incluído como campo pelo aplicativo.

Para relatar um problema com descrição ou imagem, use o formulário de bug
separado e opt-in; suas regras estão em [Relatos de bug e privacidade](bug-reports.md).

## Relatório de falhas (Sentry)

Consentimento separado do da telemetria de uso acima:
**Ajuda-nos com relatórios automáticos de falhas** também vem habilitado por
padrão em instalações novas (mesmo raciocínio: o produto está em fase
inicial e falhas reais são difíceis de reproduzir sem esses dados), mas
**nada é enviado antes da tela de consentimento** ser confirmada
explicitamente pelo usuário, mesmo com o padrão ativado — ver a seção
"Consentimento" acima e `PrivacyConsentEvaluator`. Instalações antigas que já
tinham telemetria configurada, mas nunca viram essa tela, também não têm
nada enviado até confirmarem.

### Dados enviados quando autorizado

Quando o aplicativo trava ou encontra uma exceção não tratada, e somente se
autorizado, envia ao Sentry:

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo e mensagem sanitizados da exceção | `IOException: could not read %APPDATA%\...` | identificar a causa técnica |
| Stack trace sanitizado | caminhos do usuário substituídos por `%APPDATA%`/`%USERPROFILE%`/etc. | localizar o ponto de falha no código |
| Versão do aplicativo | `1.0.3` | correlacionar com uma versão específica |
| Ambiente | `Development` ou `Production` | nunca mistura erros de desenvolvimento com erros de usuários finais |

O SDK do Sentry é inicializado apenas quando autorizado, com
`SendDefaultPii=false`, `AutoSessionTracking`/`CaptureFailedRequests`/
`TracesSampleRate` desligados (nenhum dado além do evento de erro em si é
enviado) e um `BeforeSend` obrigatório (`CrashReportSanitizer`) que reaplica
a mesma sanitização de caminhos já usada no relatório técnico
(`ReportSanitizer`) sobre mensagem, stack trace e qualquer dado de usuário
que o SDK tente preencher automaticamente — nome da máquina, IP e
identificador de usuário são sempre sobrescritos/limpos, nunca enviados.

### Configuração centralizada e ambientes

O DSN do Sentry não é um literal espalhado pelo código: fica em
`src/FiveMCleaner.App/Config/appsettings.Development.json` e
`appsettings.Production.json` (com `appsettings.json` como base/fallback
seguro, sem DSN). `AppEnvironment.Resolve()` decide qual arquivo usar: a
variável de ambiente `FIVEMCLEANER_ENVIRONMENT` tem prioridade (é isso que
`scripts/Start-DevelopmentApp.ps1` define como `Development`); sem ela, uma
build Debug resolve para `Development` e uma build Release (a distribuição
pública real) resolve para `Production`. Isso garante que erros do
desenvolvedor rodando localmente nunca se misturam, no Sentry, com erros de
usuários finais rodando a versão instalada — ambos usam o mesmo projeto e
DSN do Sentry, apenas com a tag `Environment` diferente.

### Cloudflare Worker/D1 (telemetria de uso, escopo futuro)

Um scaffold do Worker que receberia a telemetria de uso (não os relatórios
de falha, que vão direto ao Sentry) existe em `infra/cloudflare-worker/`,
com validação server-side e schema D1 — documentado em seu próprio
`README.md`. Ele **não está implantado** e o cliente .NET ainda **não**
envia dados para ele: a telemetria de uso continua sendo enviada pelo
FormSubmit, sem alteração, até uma etapa futura trocar o transporte.
