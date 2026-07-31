# Redesign visual completo (WPF-UI / Fluent Design) — 31/07/2026

## Objetivo

Reformular toda a interface visual do FiveMCleaner (WPF) para um visual
moderno, leve, responsivo e profissional, com identidade dark + acento laranja
FiveM, usando o design system Fluent do Windows 11 via biblioteca WPF-UI.
Escopo é puramente visual/UX — nenhuma mudança de lógica de negócio, fluxo de
otimização, ViewModels ou contratos.

## Contexto atual

- `Themes/Palette.xaml` já define uma paleta dark com acento laranja
  (`ColorOrange #FF7A18`) e cores de estado (verde/azul/amarelo/vermelho).
- `Themes/Controls.xaml` define estilos de controles nativos WPF (botões,
  toggle, scrollbar, etc.) do zero, sem biblioteca externa.
- `MainWindow.xaml`: janela `WindowStyle="None"` com `WindowChrome` manual,
  sidebar de navegação própria, DataTemplates inline (`ActionTemplate`,
  `LogTemplate`, `StepLedgerTemplate`, `ReportLineTemplate`).
- Três janelas secundárias com layout independente, reaproveitando os mesmos
  brushes mas sem componentização compartilhada:
  `Views/OptimizationConfirmationWindow.xaml`, `Views/BugReportWindow.xaml`,
  `Views/PrivacyConsentWindow.xaml`.
- Suporte de tema claro/escuro/sistema já existe na camada de preferências do
  app (fora do escopo desta mudança — o redesign deve preservar essa
  capacidade, não removê-la).

## Decisões

1. **Biblioteca**: adotar `WPF-UI` (Lepo.co) como dependência NuGet do
   `FiveMCleaner.App`. Fornece `FluentWindow`, `NavigationView`, `CardControl`/
   `CardExpander`, `Button`, `ToggleSwitch`, `InfoBar`, backdrop Mica/Acrylic e
   sistema de tema nativo.
2. **Identidade de cor**: a paleta atual (`Palette.xaml`) é preservada como
   fonte da verdade; o accent color do WPF-UI é mapeado para
   `ColorOrange`/`OrangeGradientBrush` existentes, não uma paleta nova do
   zero. Adiciona-se um brush "glow" (mesma cor, uso em `DropShadowEffect`)
   para hover/estado ativo — reforço "gamer" sem introduzir paleta cyberpunk
   genérica.
3. **Janela principal**: `MainWindow` migra para `ui:FluentWindow`,
   `WindowBackdropType="Mica"`, chrome padrão do WPF-UI substitui o
   `WindowChrome` manual atual.
4. **Navegação**: sidebar atual é substituída por `ui:NavigationView`
   (collapse/expand, seleção animada, indicador de item ativo com barra de
   acento laranja, ícones Segoe Fluent).
5. **Componentização**: `ActionTemplate`, `LogTemplate`, `StepLedgerTemplate`,
   `ReportLineTemplate` e demais cards migram para `ui:CardControl`/
   `CardExpander` com estilo compartilhado, em vez de `Border` inline
   duplicado por template.
6. **Motion**: hover em cards eleva levemente (scale ~1.02 + sombra, ~150ms);
   botões primários ganham glow animado no hover e leve scale-down no clique;
   troca de página usa a transição padrão do `NavigationView` (slide+fade);
   barra de progresso do Otimizador ganha gradiente animado enquanto em
   execução.
7. **Janelas secundárias**: as três janelas (`OptimizationConfirmationWindow`,
   `BugReportWindow`, `PrivacyConsentWindow`) migram para `ui:FluentWindow` e
   reusam os mesmos estilos de `Card`/`Button`, eliminando divergência visual
   com a janela principal.
8. **Tipografia**: mantém Segoe UI Variable (já em uso), sem fonte embutida
   nova.

## Fora de escopo

- Lógica de `ViewModels`, fluxo de otimização, contratos, testes de
  comportamento.
- Ícone/asset do aplicativo (arquivo em si).
- Textos e localização (pt-BR/en).
- Remoção do suporte a tema claro/escuro/sistema — deve continuar funcionando
  através do sistema de tema do WPF-UI.

## Validação

- Build Release sem avisos.
- Suíte de testes .NET existente permanece verde (mudança é apenas em XAML/
  App — nenhum teste deve depender de layout visual específico; se algum
  depender, ajustar o teste, não pular).
- Verificação visual manual: abrir o atalho de desenvolvimento
  (`scripts/Start-DevelopmentApp.ps1`), navegar por todas as abas/páginas e
  abrir as três janelas secundárias, em tema claro e escuro.
- `Verify-Safety.ps1` aprovado (mudança não deve tocar nada fora de
  `FiveMCleaner.App`/`Themes`/`Views`).
