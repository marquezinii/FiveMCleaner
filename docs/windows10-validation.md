# Validação manual — Windows 10

Use uma máquina física ou VM x64 com Windows 10 versão 2004 (build 19041) ou mais recente. Registre a edição e o build testados.

## Fluxo básico

- Instale pelo `Ralven-Setup-*-win-x64.exe` sem elevação e confirme abertura, encerramento, bandeja e nova ativação da instância já aberta.
- Confira o tema claro, escuro e do sistema; a janela principal e os diálogos devem usar Acrylic, sem área transparente ou controles inacessíveis.
- Verifique 100%, 125%, 150% e, se disponível, mover a janela entre monitores com escalas diferentes; texto, título, botões e diálogos não podem cortar nem ficar desfocados.
- Desative **Efeitos de animação** no Windows e confirme que transições do Ralven ficam instantâneas; reative-os e confirme a volta das transições.

## Integrações Windows

- Execute diagnóstico, monitor local e painel de desempenho em uma máquina sem contador de GPU disponível; a tela deve informar indisponibilidade sem falhar.
- Confirme leitura de segurança, firewall e atualizações; quando a API não reportar um provedor, o estado precisa ser explícito como indisponível/parcial.
- Aplique e restaure os controles de jogos com o FiveM fechado; repita com o FiveM aberto para confirmar bloqueio seguro.
- Execute uma ação que peça UAC, cancele uma vez e conclua outra; verifique o resultado semântico e o rollback correspondente.

## Conta, atualização e recuperação

- Faça login, logout e login Google com o navegador padrão; confirme o retorno loopback e a recuperação da janela.
- Verifique aviso de atualização com e sem ícone de bandeja já visível.
- Instale uma atualização e confirme staging, relançamento, health receipt, notas da versão e rollback ao simular uma inicialização não saudável.
- Em uma instalação sem WinGet, abra Aplicativos e confirme indisponibilidade clara, sem tentativa de baixar ou executar ferramenta adicional.
