# Streaming safety

FiveMCleaner possui uma fundação local e somente leitura para reconhecer OBS
Studio, Streamlabs Desktop e TikTok LIVE Studio. O detector existe para que uma
integração futura possa proteger o fluxo de trabalho de criadores durante uma
otimização.

## Painel de prontidão para criadores

O dashboard transforma o levantamento local em um painel de três sinais, sem
aplicar alterações no software de transmissão:

1. **Software preservado**: mostra os aplicativos reconhecidos ou em execução.
   A presença de um processo nunca é tratada como prova de que há uma live.
2. **Margem de recursos**: usa memória disponível e pressão já medida pelo
   diagnóstico para sugerir um teste privado quando a margem é pequena. Não
   inventa FPS, bitrate, resolução ou capacidade de encoder.
3. **Estado da sessão de jogo**: indica se FiveM ou GTA V estão abertos, porque
   eles devem ser fechados antes de uma otimização que possa editar arquivos.

O painel serve tanto para quem transmite em Twitch, YouTube, TikTok ou outra
plataforma quanto para quem apenas grava. Ele não depende da plataforma, não
envia telemetria e não requer login. Em caso de pressão ou leitura parcial, a
orientação é testar uma cena privada e observar a própria plataforma, não
alterar silenciosamente configurações de encoder.

## O que o detector informa

Para cada aplicativo conhecido, o detector mantém dois sinais independentes:

- `IsInstalled`: existe evidência allowlisted no registro de desinstalação ou
  em um caminho de instalação conhecido;
- `IsProcessRunning`: um nome exato de processo allowlisted foi observado.

Um processo em execução **não significa que a pessoa está ao vivo**. O modelo
não expõe uma propriedade `IsLive` e qualquer UI futura deve usar uma frase
como "software de transmissão em execução; uma transmissão pode estar ativa".

Se o Windows negar acesso a um processo ou chave de registro, o detector não
falha e não tenta elevar privilégios. Ele marca o levantamento como parcial para
que ausência de evidência não seja apresentada como ausência confirmada.

## Limites de privacidade e segurança

O detector:

- não encerra, suspende ou altera prioridade de processos;
- não lê linha de comando, módulos, janelas ou conexões de rede;
- não lê nem modifica cenas, perfis, logs, plugins ou configurações;
- não acessa stream keys, contas, tokens, microfone ou câmera;
- não altera firewall, antivírus, HAGS, encoder ou resolução de saída;
- não faz varredura recursiva de disco;
- não envia telemetria e não consulta serviços remotos.

Os nomes de processo e produto usam correspondência exata, sem heurística por
substring. Os caminhos consultados são candidatos fixos e conhecidos. Instalações
portáteis ou em diretórios personalizados podem aparecer somente quando o
processo estiver aberto; essa limitação é preferível a uma busca invasiva.

## Diretrizes para integração futura

Ao encontrar um aplicativo de transmissão em execução, uma integração deve:

1. preservar o aplicativo e todos os seus arquivos;
2. nunca afirmar que existe uma live ativa;
3. evitar reinícios ou mudanças que interrompam captura e áudio;
4. desabilitar apenas captura histórica duplicada do Windows quando essa ação
   fizer parte do perfil selecionado e puder ser desfeita;
5. apresentar ajustes de encoder, FPS e bitrate como recomendações, não como
   mutações silenciosas;
6. medir desempenho local antes de prometer qualquer ganho.

## Fontes oficiais

- [OBS: parâmetros de inicialização e nome `obs64.exe`](https://obsproject.com/kb/launch-parameters)
- [OBS: seleção de GPU no Windows](https://obsproject.com/kb/gpu-selection-guide)
- [OBS: diagnóstico de sobrecarga de codificação](https://obsproject.com/kb/encoding-performance-troubleshooting)
- [OBS: codificação por hardware](https://obsproject.com/kb/hardware-encoding)
- [OBS: HAGS pode causar falhas com encoder por hardware](https://obsproject.com/kb/hags)
- [Streamlabs: caminho padrão de `Streamlabs Desktop.exe`](https://support.streamlabs.com/hc/en-us/articles/360044254233-Troubleshooting-Capture-Source-issues-on-Laptops)
- [TikTok LIVE Studio: cenas, fontes e monitoramento do sistema](https://www.tiktok.com/live/studio/help/article/Get-started-with-your-first-LIVE/Learn-the-basics-of-LIVE)
- [Twitch: Broadcast Health e Twitch Inspector](https://help.twitch.tv/s/article/guide-to-broadcast-health?language=en_US)
- [YouTube: configurações oficiais de encoder e teste de upload](https://support.google.com/youtube/answer/2853702)

Essas fontes sustentam as recomendações de proteção e diagnóstico. Elas não
autorizam inferir o estado de uma transmissão apenas pela existência do processo.
