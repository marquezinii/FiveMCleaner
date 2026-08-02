# Diagnósticos essenciais, dados opcionais e privacidade

Para contrato, troubleshooting e checklist de release da infraestrutura, veja
[Operação da telemetria em produção](telemetry-operations.md).

## Consentimento

Quando uma versão altera a política, a tela de transparência volta a aparecer
e só pode ser fechada por **Continuar**. Sem mudança na política, a escolha
salva não é perguntada de novo.

**Diagnósticos essenciais** permanecem ativos: versão do app e do Windows,
arquitetura, crashes, eventos do atualizador e resultado técnico das
otimizações. Não incluem HWID, MAC, serial, nome do PC, usuário do Windows ou
caminhos locais.

A opção **Ajudar a melhorar o FiveMCleaner** fica em **Configurações**, vem
habilitada por padrão em instalações novas e pode ser desligada a qualquer
momento. Ela controla apenas hardware, perfil e recursos usados.

## Dados opcionais enviados quando autorizados

Ao término, falha ou cancelamento de uma otimização, o aplicativo monta um
evento técnico com estes campos (versão 2 do consentimento de privacidade —
ver `PrivacyConsentPolicy`):

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo | `optimization-completed` | distinguir conclusão, falha ou cancelamento |
| Tempo de execução | `18342` ms | identificar operações anormalmente longas |
| Versão | `1.1.0` | correlacionar comportamento com uma versão |
| Categoria de erro | `timeout` | presente apenas em falhas; é uma lista fechada |
| Versão do Windows e arquitetura | `Windows 11`, `x64` | estatística agregada de compatibilidade |
| Modelo de CPU e GPU | `AMD Ryzen 5 5600X`, `NVIDIA GeForce RTX 5070` | estatística agregada de hardware mais comum |
| Faixa de RAM | `32` GiB (arredondada para cima entre um conjunto fixo de faixas) | estatística agregada, nunca o valor exato do sistema |
| Perfil escolhido | `Balanced` | popularidade de cada modo (Leve/Médio/Agressivo) |
| Identificadores das ações aplicadas | `fivem.legacy.cache.repair` | funcionalidade mais usada, agregada |

As únicas categorias de erro possíveis são `cancelled`, `timeout`,
`access-denied`, `io`, `invalid-data` e `unexpected`. Mensagens de exceção,
stack traces, nomes de arquivos e caminhos locais nunca entram nesse contrato.
Modelo de CPU/GPU e faixa de RAM são os mesmos dados já mostrados no
diagnóstico local do app — categorias de hardware compartilhadas por muitas
máquinas, nunca um identificador único (número de série, MAC, GUID de
hardware). O transporte é o Worker Cloudflare
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

O código limita os nomes de evento e categorias a uma allowlist e recusa
campos fora desse esquema. Falhas de rede são ignoradas: não interrompem a
otimização, não geram nova telemetria e não são reenviadas automaticamente.
A fila local (`LocalTelemetryQueue`) persiste eventos pendentes por até 14
dias antes de descartá-los, para sobreviver a reinícios e períodos offline
sem crescer indefinidamente.

## Destino e metadados de transporte

A telemetria é enviada ao endpoint HTTPS do Worker Cloudflare (rota
`/telemetry`, ver abaixo) — esse é o único transporte, o FormSubmit não é
mais usado para nada relacionado a telemetria de uso. O payload do
FiveMCleaner não contém dados pessoais. Como em qualquer conexão HTTPS, a
infraestrutura de rede pode processar metadados de conexão, como endereço
IP, conforme suas próprias políticas; isso não é controlado nem incluído
como campo pelo aplicativo.

Para relatar um problema com descrição ou imagem, use o formulário de bug
separado e opt-in; suas regras estão em [Relatos de bug e privacidade](bug-reports.md).

## Relatório de falhas (Sentry)

Relatórios de crash fazem parte dos diagnósticos essenciais e seguem a mesma
sanitização e minimização descritas nesta página.

### Dados enviados quando autorizado

Quando o aplicativo trava ou encontra uma exceção não tratada, e somente se
autorizado, envia ao Sentry:

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo e mensagem sanitizados da exceção | `IOException: could not read %APPDATA%\...` | identificar a causa técnica |
| Stack trace sanitizado | caminhos do usuário substituídos por `%APPDATA%`/`%USERPROFILE%`/etc. | localizar o ponto de falha no código |
| Versão do aplicativo | `1.1.0` | correlacionar com uma versão específica |
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

O painel administrativo privado consome esses endpoints para
mostrar gráficos agregados — otimizações por dia, versões do Windows/app,
funções mais usadas, hardware mais comum, tempo médio, taxa de sucesso e,
para investigar bugs mais rápido, erros por categoria, ações mais
associadas a falhas, um feed não agregado dos últimos erros e uma aba
**"Bugs reportados"** com os relatos recebidos pela rota `/bugs` (categoria,
resumo, versão, perfil, ambiente, e-mail opcional e se um trecho de log foi
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
