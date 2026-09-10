# Janelas secundárias e diálogos

`Controls/DialogWindow` e `Themes/Dialogs.xaml` definem a superfície modal do
Ralven. Cada tela fornece conteúdo e ações; o shell fornece título, fechar,
atenuação da janela proprietária, foco, Escape, apresentação e limites do monitor.
Não adicionar outra barra de título, contorno externo ou botões de minimizar e
maximizar ao conteúdo. Os formulários usam as variantes `Dialog*Style` de
`Themes/Controls.xaml`, preservando os templates e estados existentes quando
possível. Seleção usa preenchimento; foco nas opções e ações secundárias usa
uma pequena marca inferior, sem uma caixa branca ao redor do controle.

## Modelo de interação

Mantemos `Window.ShowDialog` e `Owner` em vez de reimplementar modalidade em um
overlay: o Windows bloqueia a janela proprietária, mantém acessibilidade de
janela, ordem de ativação e retorno síncrono de `DialogResult`. O backdrop é um
adorner apenas visual, removido quando o diálogo fecha. Clicar fora não fecha
nem descarta um formulário. Não há entrada própria na barra de tarefas.

| Experiência | Composição e comportamento |
| --- | --- |
| Login, cadastro, recuperação, verificação e conclusão de perfil | Uma superfície de conta centralizada, sem arraste. Login usa 600×620 DIPs; cadastro cresce até 600×780, respeitando o monitor. Corpo rolável, ações fixas. |
| Relatar um bug | Formulário de 820×820 DIPs, redimensionável e movimentável pelo header. Categoria, motivo, resumo e descrição são prioritários; log e e-mail ficam em detalhes opcionais. |
| Criar/alterar senha | Formulário de 600×600 DIPs com rolagem e ações fixas. Não fecha durante a operação; sucesso preserva o resultado modal. |
| Privacidade | Superfície centralizada de 680×660 DIPs. Preserva a decisão obrigatória: fechar/Escape/Alt+F4 não confirmam nem ignoram consentimento. |
| Notas da versão | Leitor de 760×720 DIPs, redimensionável, com rolagem e ação de fechar fixa. |
| Termos de uso | Leitor de 820×800 DIPs, redimensionável. Pode abrir sobre o cadastro e retornar a ele sem perder seus campos. |
| Confirmações e avisos | Superfície de 620×320 DIPs, texto rolável e ações que podem quebrar linha. A confirmação nunca é o botão padrão; cancelar recebe foco inicial. |

As medidas são preferidas, não limites rígidos: o shell converte a área útil do
monitor para DIPs, desconta margem e reduz também os mínimos quando necessário.
Reavalia limites ao mudar o DPI. Longos conteúdos rolam; ações não pertencem ao
mesmo `ScrollViewer`. Redimensionamento usa `WindowChrome` e a modalidade
continua sob responsabilidade do Windows.

As confirmações de atualização, exclusão de conta, restauração de histórico,
indisponibilidade e falha ao abrir conteúdo externo usam o mesmo diálogo já
compartilhado por otimização, aplicativos, sistema e assinatura. Seletores de
arquivo/pasta continuam nativos, assim como o aviso fatal da inicialização:
este último precisa funcionar quando os recursos visuais do app falham.

## Teclado e movimento

- Tab e Ctrl+Tab ficam na superfície modal. O conteúdo recebe foco inicial e
  o shell restaura o foco anterior ao fechar.
- Escape fecha um diálogo dispensável; com seletor aberto, primeiro permite
  fechar a lista. Fechamento também respeita a política de operação/consentimento.
- Campos mantêm nomes de automação, seleção, revelação de senha e validação.
- A entrada usa fade de 140 ms, desativado quando animações do Windows estão
  desabilitadas ou em alto contraste. O fechamento é imediato: não atrasamos
  cancelamento, retorno do resultado ou persistência de consentimento para
  exibir uma animação.

## Verificação reproduzível

```powershell
dotnet build src/Ralven.App/Ralven.App.csproj --configuration Release -p:RalvenDialogProbe=true
dotnet artifacts/dialog-probe/net10.0-windows10.0.19041.0/Ralven.dll artifacts/dialog-captures
```

O probe abre os XAML reais em 32 combinações (oito estados, claro/escuro,
tamanho normal/480×520), salva imagens e verifica modalidade, Tab, Escape,
termos aninhados, seletor, rolagem multiline, consentimento e confirmação
conservadora. Não executa o startup do produto, autenticação, envio de bug,
escrita de preferências ou chamadas de rede. As credenciais e textos dos testes
são sintéticos. Os testes `DialogWindowTests` cobrem conversão de área útil em
100%, 125%, 150% e 200% de escala; isso não substitui mover a janela entre
monitores físicos com DPIs diferentes nem uma sessão com leitor de tela.
