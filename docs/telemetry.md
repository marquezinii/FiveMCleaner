# Telemetria opcional e privacidade

## Consentimento

A opção **Ajude a melhorar o FiveMCleaner** fica em **Configurações** e vem
desativada por padrão, inclusive após atualização. Sem o toggle ligado, o
aplicativo não cria nem envia eventos de telemetria. O usuário pode desligá-lo
a qualquer momento; eventos futuros deixam de ser enviados imediatamente.

## Dados enviados quando autorizados

Ao término, falha ou cancelamento de uma otimização, o aplicativo envia por
HTTPS somente estes campos de um evento técnico:

| Campo | Exemplo | Finalidade |
| --- | --- | --- |
| Tipo | `optimization-completed` | distinguir conclusão, falha ou cancelamento |
| Tempo de execução | `18342` ms | identificar operações anormalmente longas |
| Versão | `1.0.2` | correlacionar comportamento com uma versão |
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
