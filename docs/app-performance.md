# Desempenho do aplicativo

Para fluxo de abertura, splash, gargalos e profiling do processo até a
prontidão local, veja [Inicialização do Ralven](startup-performance.md).

O painel ao vivo coleta somente com a Visão geral selecionada e a janela ativa,
visível e não minimizada. Perda de foco, minimização, navegação e bandeja pausam
o timer e cancelam a captura em andamento. Reabrir obtém uma leitura nova;
ativação repetida não dispara coletas extras. Uma captura cancelada não publica
resultado nem indisponibilidade depois de uma retomada rápida.

CPU, GPU, memória, disco e rede mantêm no máximo 60 amostras locais. Os cinco
minigráficos e o gráfico selecionado só redesenham quando uma dessas amostras
chega, a 1 Hz; não existe laço de animação. Percentuais permanecem na escala
fixa de 0–100%. Rede e memória absoluta usam uma escala legível calculada a
partir da janela atual, sempre acompanhada da unidade.

O alvo FiveM fica disponível somente após detectar uma instalação Legacy. Ele
substitui a coleta geral por uma enumeração local dos processos cuja imagem foi
validada dentro dessa raiz e mostra apenas CPU e working set agregado. A primeira
leitura de CPU estabelece a base; intervalos maiores que três segundos são
descartados para que tempo em segundo plano não reapareça como uso atual. GPU,
disco, rede, FPS e frame time por processo permanecem indisponíveis em vez de
serem estimados.

O monitor local de sessão continua opt-in e somente leitura, com a mesma
cadência de cinco segundos e validações de identidade/caminho. Pausar as
métricas não pausa esse monitor, nem operações explícitas de otimização,
cancelamento, rollback, atualização ou acompanhamento pessoal. Estado novo
continua atualizando as restrições do otimizador na bandeja; rodadas iguais
dispensam a atualização da apresentação oculta.

## Custo da coleta

`WindowsResourceUsageInspector` lê a categoria GPU Engine duas vezes por
amostra, em vez de reler todos os dados para cada instância de contador. A
comparação usa nomes de instâncias presentes nas duas amostras e
`CounterSample.Calculate`. Instâncias recém-criadas/desaparecidas sem par não
geram uma estimativa. CPU e disco mantêm PDH com nomes ingleses independentes
do idioma do Windows; rede divide a diferença de bytes pelo tempo efetivamente
decorrido. Cancelamento atravessa as esperas e libera consultas abertas.

A Microsoft documenta que [ler a categoria inteira pode custar o mesmo que ler
um único contador](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.performancecountercategory.readcategory?view=windowsdesktop-10.0).
Essa propriedade é relevante para GPU Engine, que possui uma instância por
combinação de processo/engine, e pode conter centenas delas.

Não há limpeza forçada de working set, `GC.Collect`, alteração de prioridade
ou mudança de configuração do Windows para reduzir o número mostrado no
Gerenciador de Tarefas. O trabalho e as alocações são reduzidos na origem.

## Medição reproduzível

Em uma sessão Windows interativa, a partir da raiz:

```powershell
dotnet run --project src/Ralven.App/Ralven.App.csproj --configuration Release -p:RalvenPerformanceProbe=true -- --demo-synthetic --verify artifacts/performance-after.json
```

O flag de build inclui `scripts/PerformanceProbe.cs` somente no executável de
medição, em `artifacts/performance-probe/`; builds normais não contêm esse
entry point. O harness abre uma única janela WPF real, com diagnóstico sintético
e preferências em memória, enquanto as métricas e a enumeração de processos são
leituras reais do Windows. O monitor usa uma raiz de teste vazia sob `artifacts`,
sem abrir ou modificar uma instalação FiveM real. Não há login, telemetria,
update remoto ou aplicação de otimizações nessa execução.

Após 12 segundos de aquecimento, cada fase tem três segundos de acomodação e
20 segundos de medição: primeiro plano, perda de foco, minimizada, bandeja,
bandeja com monitor ligado e janela restaurada. O JSON é atualizado após cada
fase, e uma captura PNG da janela restaurada é salva ao lado. `--verify` exige
estado real da janela estável durante todo o intervalo, amostras em primeiro
plano/restaurada e zero amostras nas outras fases. `windowStateStable` e
`collectionPolicySatisfied` marcam explicitamente as condições de validade no
JSON, inclusive em uma fase rejeitada com exit code 1. Para
medir uma base anterior que ainda não respeita essa regra, omita `--verify` e
use o mesmo harness e configuração de build.

Evite interagir com as janelas de teste ou executar builds/testes/cargas durante
a medição. Outros aplicativos e agentes também podem interferir nos resultados.
CPU é o tempo total de processador consumido pelo processo, em milissegundos;
divida por duração × 1.000 para obter a fração de um núcleo lógico. As alocações
são bytes gerenciados acumulados durante a fase, não memória residente.
`workingSetMiB` mede páginas residentes e `privateMiB` memória privada comprometida;
ambos variam com GC, driver, resolução, SO e pressão de memória do computador.

Uma execução curta de demonstração não mede FPS, stutter ou impacto numa partida,
nem comprova o consumo de uma instalação autenticada em PC de entrada. Os testes
determinísticos cobrem presença/ausência/falha, descarte de resultados antigos e
restrições do otimizador; validar com FiveM real continua sendo uma etapa de uso.

## Rodada de 09/09/2026

Comparação local em Release contra `efa9c8e`, com o mesmo harness e 20 segundos
por fase. Valores arredondados; CPU é tempo de processador do processo, não uma
porcentagem do computador inteiro.

| Cenário | CPU antes/depois (ms) | Alocações antes/depois (MiB) | Coletas antes/depois |
| --- | ---: | ---: | ---: |
| Painel coletando | 3.250 / 1.156 | 485,87 / 22,33 | 20 / 20 |
| Janela sem foco | 3.625 / 375 | 485,62 / 2,06 | 20 / 0 |
| Minimizada | 3.188 / 0 | 482,60 / 0,001 | 20 / 0 |
| Bandeja, monitor desligado | 31,25 / 0 | 0,014 / 0,001 | 0 / 0 |
| Bandeja, monitor ligado | 62,50 / 109,38 | 3,25 / 3,31 | 0 / 0 |
| Retomada, monitor ligado | 3.125 / 578 | 485,66 / 26,14 | 20 / 20 |

O painel produziu aproximadamente 95,4% menos alocações e consumiu 64,4% menos
tempo de CPU nessa janela de medição. A base continuava coletando mesmo quando
perdia foco, inclusive nas fases rotuladas como painel/retomada; por isso essas
linhas comparam 20 coletas, não um benchmark isolado de renderização ativa.
A execução nova passou as verificações de pausa/retomada e manteve o estado da
janela estável em todas as seis fases. Uma captura WPF restaurada foi
inspecionada, com métricas disponíveis e monitor aguardando FiveM.

Com monitor na bandeja, memória privada comprometida foi de 512,35 para
521,52 MiB, e o working set foi de 216,24 para 219,49 MiB. Uma execução anterior
da versão otimizada havia registrado 433,30 MiB privados, 223,04 MiB residentes
e 31,25 ms de CPU nesse cenário, mostrando a variação entre execuções. Portanto
**não houve redução comprovada de RAM residente nem do custo do monitor de
sessão nessa amostra**. Zero CPU registrado reflete
a resolução do contador nesse intervalo; não promete custo absoluto zero.
O resultado sustenta redução de trabalho e alocações, sem promessa universal de
memória mínima, FPS ou impacto em qualquer máquina.

## Validação do painel seletivo em 09/09/2026

O mesmo harness, após o painel ganhar seleção e minigráficos, aprovou as seis
fases e a estabilidade da janela. Em 20 segundos, primeiro plano e retomada
registraram 20 amostras; sem foco, minimizada e as duas fases de bandeja
registraram zero. A fase de bandeja sem monitor alocou 0,003 MiB; com o monitor
opt-in, 2,863 MiB. Primeiro plano consumiu 859,375 ms de CPU e alocou 34,706 MiB;
na retomada foram 515,625 ms e 37,091 MiB. Esses valores descrevem uma execução
local e comprovam a política de suspensão, não um limite universal de consumo.
