# Inicialização do Ralven

## Fluxo e prontidão

O launcher primeiro reconcilia o journal de atualização, valida o piso de
versão, seleciona o runtime e inicia o processo. A confirmação de saúde continua
ligada ao carregamento da janela principal, sem esperar rede ou uma varredura
lenta. Uma falha na inicialização da janela invalida o recibo. Assinatura,
integridade, ativação e rollback não foram relaxados.

A supervisão do launcher permanece no thread que adquiriu o mutex de ciclo de
vida. Seus eventos são registrados localmente e enfileirados atomicamente;
somente depois de terminar a supervisão e liberar o mutex acontece o envio
remoto. Os demais consumidores de `UpdaterDiagnostics.RecordAsync` mantêm o
envio imediato. Consentimento, HTTPS/TLS, endpoint e retenção são os mesmos.

No processo WPF:

1. Carregar os recursos da aplicação e adquirir a instância única.
2. Iniciar o controlador da splash; importar dados legados fora da UI, antes
   de qualquer leitura de estado que dependa da importação.
3. Iniciar configurações, diagnóstico e histórico antes da construção do XAML.
   Leituras independentes se sobrepõem à construção; continuations da
   apresentação voltam ao Dispatcher principal.
4. Aplicar preferências/consentimento assim que as configurações estiverem
   disponíveis, mesmo se o diagnóstico falhar. Preparar diagnóstico, histórico,
   plano e workspace pessoal; um diagnóstico indisponível continua bloqueando
   as operações que dependem dele e permite nova tentativa.
5. Mostrar a janela, confirmar saúde, terminar a preparação local, aplicar
   tema/idioma e permitir renderização. Encerrar a splash antes de abrir os
   diálogos de privacidade/notas. A prontidão não espera o usuário ler um
   indicador de carregamento: não existe duração mínima.
6. Ativar monitoramento visível e agendar updater, avisos, restauração de conta,
   crash reporting autorizado e envio da fila em segundo plano. Acesso Pro
   continua indisponível até a autenticação/entitlement autoritativos.

O marco `startup-ready` significa estado local aplicado e interface liberada,
com os diálogos obrigatórios já resolvidos. Não significa que a rede respondeu
nem que o primeiro ponto do gráfico ao vivo chegou. Essas funções conservam
seus estados explícitos de leitura/indisponibilidade e não bloqueiam navegação,
diagnóstico, planejamento ou restauração local.

## Gargalos e decisões

- O perfil medido concentrou o custo no XAML/layout/renderização do shell,
  com reaplicações de tema reconstruindo recursos já ativos. O tema agora só
  é reaplicado quando o tema efetivo muda, inclusive após notificações do SO.
- Configurações aguardavam o diagnóstico inteiro. Agora elas são aplicadas
  antes da espera conjunta; falhas de diagnóstico não descartam preferências.
- O diagnóstico começava apenas em `Loaded`. Agora seu I/O independente se
  sobrepõe à construção da janela.
- A enumeração/ordenação de journals e abertura inicial de arquivo no histórico
  rodavam no chamador antes do primeiro `await`. Toda a leitura agora ocorre
  no pool, com recibos administrativos, cancelamento e rollback preservados.
- Métricas ao vivo competiam com a construção da janela. A primeira captura
  começa após a prontidão local; pausa/retomada por visibilidade é mantida.
- A fila remota do launcher era aguardada antes de `Process.Start`, incluindo
  tentativas HTTP com timeout de 10 segundos. O transporte saiu desse caminho;
  o teste com transporte controlado comprova que o enqueue local termina
  enquanto o envio permanece pendente. Não é uma medição de uma rede real.
- Importação legada, recuperação, diagnóstico e histórico não viraram tarefas
  opcionais. Varredura de cache conserva a medição real: zero não foi usado
  como substituto para tamanho desconhecido.
- Páginas secundárias já eram lazy, exceto Configurações embutida no shell.
  CPU/GPU já tinham cache temporário e probes paralelos; não foi criado outro
  cache. Não há container DI ou varredura de assemblies no startup auditado.
  Não foram adicionadas bibliotecas ou alteradas opções de empacotamento.

## Splash

Janela WPF de 640 × 400 DIP, limitada à área útil disponível, com logo reduzido
na decodificação, texto localizado em EN/PT-BR/ES e três pontos de opacidade
animada. Usa cores locais dos tokens do tema do Windows, sem blur, transparência
de janela, efeito pesado ou dependência nova. A janela principal conserva o
tema e idioma salvos. A política de movimento do Windows também vale na splash.

Um limiar cancelável de 180 ms evita criar a janela quando a preparação termina
muito cedo. Ele não é uma espera mínima: a conclusão fecha a splash imediatamente,
inclusive durante sua preparação. A duração depende do trabalho real.

A splash usa um Dispatcher STA próprio para continuar atendendo mensagens e
animando enquanto WPF constrói o shell principal. Recursos mutáveis não são
compartilhados entre Dispatchers. O HWND da janela principal é seu owner; não
há `Topmost` global. Fechar/Alt+F4 cancela a abertura. Sucesso, encerramento e
falha terminam o Dispatcher; erros retornam ao tratamento fatal existente,
com log local, mensagem e saída. Ativação de segunda instância durante startup
é lembrada e prevalece sobre `StartMinimized`.

A separação segue o [modelo de threads documentado pelo WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model).
O WPF também distingue custo real e resposta visual na sua
[orientação sobre tempo de inicialização](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time).

## Medição reproduzível

```powershell
dotnet build src/Ralven.App/Ralven.App.csproj -c Release -p:RalvenStartupProbe=true
./artifacts/startup-probe/net10.0-windows10.0.19041.0/Ralven.exe --demo artifacts/startup.json
./artifacts/startup-probe/net10.0-windows10.0.19041.0/Ralven.exe --demo-synthetic artifacts/startup-synthetic.json --capture
```

O probe executa `App.InitializeComponent` e `App.Run` reais. `--demo` usa
diagnóstico local somente leitura; `--demo-synthetic` isola o custo de UI com
diagnóstico sintético. Ambos usam preferências/workspace em memória, sem
importação de dados pessoais, autenticação, telemetria ou atualização remota.

O JSON registra marcos desde a criação do processo (offset inicial de relógio
mais Stopwatch monotônico), CPU do processo, alocações e heartbeat do
Dispatcher principal de 16 ms. A splash emite heartbeat de 50 ms apenas no
probe. `main-rendered` é `ContentRendered`, não uma leitura física do monitor.
O harness exige renderização e prontidão, depois confirma atendimento em
prioridade Input. Um timeout independente de 90 s encerra um probe travado.

`--capture` salva PNG da splash e da janela com o DPI efetivo. Capturar durante
o startup acrescenta trabalho; use execuções sem captura para comparar tempos.
Builds normais não contêm o entry point do probe, e os marcos não gravam nada
em disco nem enviam dados. Preserve o binário de baseline instrumentado e
alterne baseline/nova versão, sem builds/testes/cargas entre as amostras.

## Comparação local de 10/09/2026

Baseline `5863453` com os mesmos marcos de instrumentação, Release .NET SDK
10.0.303, Windows 11 build 26200, 12 processadores lógicos, DPI 150%. Dez
execuções por versão, com diagnóstico real somente leitura (`--demo`), sem
capturas, alternando a ordem antes/depois na segunda metade. Não foram feitos
builds durante as amostras, mas outras cargas da sessão não foram controladas.

| Marco/medida | Mediana antes | Mediana depois |
| --- | ---: | ---: |
| Primeira resposta visual | 1.859 ms | 1.268 ms |
| `Loaded` interno da janela | 1.349 ms | 1.475 ms |
| Janela principal renderizada | 1.859 ms | 1.701 ms |
| Prontidão + atendimento em Input | 1.860 ms | 1.729 ms |
| CPU acumulada do processo | 2.281 ms | 2.102 ms |
| Alocações gerenciadas acumuladas | 41,79 MiB | 30,83 MiB |

A primeira resposta visual caiu aproximadamente 31,8%, e a prontidão medida
7,1%. O marco interno `Loaded` ficou mais tarde nesta amostra; não é uma
melhoria em todos os marcos. A renderização e o atendimento com estado pronto
aconteceram antes. Alocações diminuíram cerca de 26,2%; não são uma medida de
RAM residente. A prontidão variou de 1.548–3.594 ms antes e 1.305–2.576 ms
depois, portanto o resultado não sustenta uma promessa universal de tempo.

| Execução | Prontidão antes (ms) | Prontidão depois (ms) | Primeira resposta depois (ms) |
| --- | ---: | ---: | ---: |
| 1 | 1591 | 1664 | 1270 |
| 2 | 1548 | 1331 | 937 |
| 3 | 2056 | 2041 | 1547 |
| 4 | 1781 | 1715 | 1267 |
| 5 | 3208 | 1305 | 944 |
| 6 | 3594 | 2031 | 1558 |
| 7 | 1922 | 2576 | 1932 |
| 8 | 1799 | 1469 | 998 |
| 9 | 3005 | 2062 | 1566 |
| 10 | 1563 | 1742 | 1260 |

O maior intervalo entre heartbeats da splash foi de 78,2 ms nas execuções
medidas, enquanto a thread principal ainda construía/renderizava o shell.
Esse indicador comprova atendimento do seu Dispatcher, não um benchmark de
FPS da animação. A captura da splash final e dos temas claro/escuro da UI foi
inspecionada em WPF real. Os pontos usam animações WPF nativas de opacidade;
imagens PNG são apenas frames estáticos.

## Limites de validação

Validações executadas nesta tarefa:

- `dotnet restore Ralven.slnx`: aprovado.
- `dotnet build Ralven.slnx --configuration Release --no-restore`: aprovado,
  zero warnings e erros.
- `dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1`:
  1.454 aprovados, zero falhas e zero ignorados.
- `./scripts/Verify-Safety.ps1 -SkipTests`: aprovado.
- Probe de startup: as 20 execuções comparativas terminaram em `ready`;
  capturas adicionais em WPF real inspecionadas nos temas claro/escuro.

O probe existente `RalvenPerformanceProbe=true`, executado com
`--demo-synthetic --verify`, não concluiu a sequência de estados: a janela
perdeu foco durante a fase `foreground`, invalidando sua precondição de
atividade estável. Foram coletadas 20 amostras nessa fase, mas isso não aprova
a sequência foreground/inativa/minimizada/tray. Essa verificação interativa
permanece pendente; o check não foi removido ou enfraquecido.

Os números locais não representam um cold boot, PC de entrada, disco lento,
sessão autenticada ou instalação pública passando pelo launcher. A abertura
real do pacote pós-update, login/entitlement online e offline, primeira
importação com muitos dados, Windows 10 e múltiplos monitores/DPI precisam
de homologação nesses ambientes. Os testes automatizados cobrem os contratos
de consentimento, autenticação, atualização/rollback, instância única,
preferências, histórico e ciclo de vida da splash com dados isolados.
