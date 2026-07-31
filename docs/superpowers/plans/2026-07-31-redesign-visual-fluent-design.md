# Redesign Visual Fluent Design (WPF-UI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reformular toda a interface visual do FiveMCleaner (WPF) para Fluent
Design moderno (WPF-UI), mantendo a identidade dark + laranja FiveM, com
NavigationView, Mica, cards componentizados e motion rica — sem tocar lógica
de negócio, ViewModels ou contratos.

**Architecture:** Adiciona a biblioteca `WPF-UI` ao `FiveMCleaner.App`.
`MainWindow` vira `ui:FluentWindow` com Mica e `ui:NavigationView` no lugar da
sidebar manual (a troca de conteúdo continua manual — 4 seções nomeadas já
existentes — em vez de migrar para `Page`/`TargetPageType`, ver "Desvio de
arquitetura" abaixo). Cards migram para `ui:CardControl`/`CardExpander`. As 3
janelas secundárias migram para `ui:FluentWindow`. `ThemeManager` existente
(que já faz patch de brushes) ganha uma chamada adicional para aplicar tema e
accent color nativos do WPF-UI, mantendo o laranja FiveM como accent (não o
accent do sistema).

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows10.0.19041.0`), WPF-UI
(Lepo.co) via NuGet.

## Desvio de arquitetura (decisão desta implementação)

O `ui:NavigationView` do WPF-UI é idiomaticamente pensado para navegação por
`Page`/`TargetPageType` (Frame interno). O app hoje não usa `Page`: tem 4
seções nomeadas (`DashboardPage`, `OptimizerPage`, `HistoryPage`,
`SettingsPage`, todas `Grid`/`ScrollViewer` na mesma célula, alternadas via
`Visibility` em `MainWindow.xaml.cs::Navigate`). Migrar para `Page` reais
exigiria quebrar um único `MainWindow.xaml`/`.xaml.cs` monolítico em 4+
arquivos novos — refatoração estrutural grande, fora do que o spec pediu
(mudança "puramente visual", sem tocar ViewModels/fluxo). Este plano usa
`ui:NavigationView` apenas como controle de apresentação (itens, ícones,
seleção, collapse), mantendo `SelectionChanged` no code-behind chamando o
mesmo `Navigate(...)` já existente. Isso entrega 100% do visual pedido
(NavigationView Fluent, animação de seleção, ícones, colapsável) sem o split
de arquitetura. Se no futuro quiser navegação por `Page`, tratar como projeto
separado.

## Adaptação do ciclo de teste

Este é um redesign de XAML/apresentação, sem lógica nova testável por unidade.
Cada tarefa usa como "teste":
1. `dotnet build` da solução sem erros/avisos novos.
2. Abertura via `scripts/Start-DevelopmentApp.ps1` e checagem visual manual do
   que a tarefa mudou (passo a passo descrito em cada tarefa).
A suíte `dotnet test` completa só precisa rodar na tarefa final (nenhuma
tarefa intermediária deve quebrá-la, já que nenhuma altera ViewModels/lógica).

## Global Constraints

- Nenhuma mudança em `ViewModels/`, `FiveMCleaner.Core`, `FiveMCleaner.Windows`,
  `FiveMCleaner.Broker`, `FiveMCleaner.Contracts` ou testes de comportamento.
- Preservar suporte a tema Claro/Escuro/Sistema (`AppThemePreference`) —
  `ThemeManager` continua sendo a fonte da verdade, só ganha uma chamada
  adicional ao WPF-UI.
- Preservar textos/localização (`LocalizedStrings`) — nenhuma string nova de
  UI deve ficar hardcoded fora do dicionário de localização existente.
- Manter Segoe UI Variable como fonte (`AppFont`), nenhuma fonte embutida.
- Accent color do WPF-UI é sempre `ColorOrange` (`#FF7A18` dark / `#E85D04`
  light) — nunca `systemAccentColor: true` do Windows.
- Cada tarefa termina com commit local próprio (por `AI_RULES.md`, commit
  local não é operação remota e é obrigatório ao final de cada tarefa
  concluída).
- Build Release sem avisos e `scripts/Verify-Safety.ps1` aprovado são
  obrigatórios na tarefa final (Task 9); rodar `dotnet build` (Debug é
  suficiente) a cada tarefa intermediária para feedback rápido.

---

### Task 1: Adicionar WPF-UI e registrar recursos base

**Files:**
- Modify: `src/FiveMCleaner.App/FiveMCleaner.App.csproj`
- Modify: `src/FiveMCleaner.App/App.xaml`

**Interfaces:**
- Produces: dicionário de recursos do WPF-UI mesclado globalmente
  (`Wpf.Ui.Controls`/`Wpf.Ui.Appearance` disponíveis em qualquer XAML do
  projeto via `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`).

- [ ] **Step 1: Adicionar o pacote NuGet**

```bash
dotnet add src/FiveMCleaner.App/FiveMCleaner.App.csproj package WPF-UI
```

Isso adiciona uma linha `<PackageReference Include="WPF-UI" Version="..." />`
dentro do primeiro `<ItemGroup>` de `FiveMCleaner.App.csproj` (junto de
`System.Management`/`Sentry`).

- [ ] **Step 2: Mesclar o dicionário de controles do WPF-UI em `App.xaml`**

Ler `src/FiveMCleaner.App/App.xaml` primeiro para localizar o
`<ResourceDictionary.MergedDictionaries>` existente (que já referencia
`Themes/Palette.xaml` e `Themes/Controls.xaml`) e adicionar a entrada do
WPF-UI **antes** de `Controls.xaml` (para que os estilos próprios do app
continuem podendo sobrescrever o que for necessário):

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="pack://application:,,,/Wpf.Ui;component/Resources/Wpf.Ui.xaml" />
    <ResourceDictionary Source="Themes/Palette.xaml" />
    <ResourceDictionary Source="Themes/Controls.xaml" />
</ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 3: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros (o dicionário do WPF-UI carregado sozinho não
quebra nada, pois nenhum XAML usa `ui:` ainda).

- [ ] **Step 4: Commit**

```bash
git add src/FiveMCleaner.App/FiveMCleaner.App.csproj src/FiveMCleaner.App/App.xaml
git commit -m "feat(app): add WPF-UI package and base resource dictionary"
```

---

### Task 2: Ponte de tema — accent color FiveM no WPF-UI

**Files:**
- Modify: `src/FiveMCleaner.App/Services/ThemeManager.cs:152-174` (método
  `ApplyEffectiveTheme`)

**Interfaces:**
- Consumes: `Wpf.Ui.Appearance.ApplicationThemeManager`,
  `Wpf.Ui.Appearance.ApplicationAccentColorManager`,
  `Wpf.Ui.Appearance.ApplicationTheme`, `Wpf.Ui.Controls.WindowBackdropType`
  (pacote adicionado na Task 1).
- Produces: `ThemeManager.ApplyEffectiveTheme(bool useLightTheme)` passa a
  também deixar os recursos nativos do WPF-UI (`ApplicationAccentColorManager`,
  `ApplicationThemeManager`) sincronizados com o tema/accent do app. Nenhuma
  assinatura pública muda — `ThemeManager.Apply(AppThemePreference)` continua
  igual, usado por `MainWindow.xaml.cs:480`.

- [ ] **Step 1: Adicionar os `using` necessários no topo do arquivo**

```csharp
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
```

- [ ] **Step 2: Estender `ApplyEffectiveTheme` para aplicar tema + accent do WPF-UI**

Local exato: dentro de `ApplyEffectiveTheme(bool useLightTheme)`, logo após o
bloco que já seta `HeroGradientBrush` (linhas 163-171) e antes de
`IsLightTheme = useLightTheme;`:

```csharp
        var accent = useLightTheme
            ? (Color)ColorConverter.ConvertFromString("#E85D04")!
            : (Color)ColorConverter.ConvertFromString("#FF7A18")!;
        var wpfUiTheme = useLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationAccentColorManager.Apply(accent, wpfUiTheme, systemGlassColor: false, systemAccentColor: false);
        ApplicationThemeManager.Apply(wpfUiTheme, WindowBackdropType.Mica, updateAccent: false);

        IsLightTheme = useLightTheme;
```

(A linha `IsLightTheme = useLightTheme;` já existente é removida do lugar
antigo e movida para o final deste bloco — não duplicar.)

- [ ] **Step 3: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 4: Checagem manual**

Abrir via `scripts/Start-DevelopmentApp.ps1`. Ir em Configurações e alternar
entre tema Claro/Escuro/Sistema. A troca deve continuar funcionando
exatamente como antes (nenhuma mudança visual perceptível ainda — esta tarefa
só prepara os recursos do WPF-UI por baixo; o efeito visual aparece nas
tarefas seguintes quando `FluentWindow`/`NavigationView`/`CardControl`
passarem a consumir esses recursos).

- [ ] **Step 5: Commit**

```bash
git add src/FiveMCleaner.App/Services/ThemeManager.cs
git commit -m "feat(app): bridge ThemeManager to WPF-UI theme and FiveM accent color"
```

---

### Task 3: MainWindow vira FluentWindow com Mica

**Files:**
- Modify: `src/FiveMCleaner.App/MainWindow.xaml:1-141` (elemento raiz e barra
  de título)
- Modify: `src/FiveMCleaner.App/MainWindow.xaml.cs` (namespace da classe base,
  se necessário)

**Interfaces:**
- Consumes: `Wpf.Ui.Controls.FluentWindow`, `Wpf.Ui.Controls.TitleBar`.
- Produces: `MainWindow` continua sendo `partial class MainWindow` — só o
  elemento raiz XAML e a classe base C# mudam de `Window` para
  `Wpf.Ui.Controls.FluentWindow`. Todo binding/`DataContext`/code-behind
  existente (`Navigate`, `ApplyTheme`, etc.) continua igual.

- [ ] **Step 1: Trocar o elemento raiz em `MainWindow.xaml`**

Ler o arquivo completo primeiro (já lido nesta sessão até a linha 598; ler o
restante antes de editar). Trocar a tag raiz `<Window ...>` (linhas 1-23) por
`<ui:FluentWindow>`, adicionando o namespace `ui` e os atributos de backdrop.
Remover `WindowStyle="None"` manual e o bloco `<shell:WindowChrome...>`
(linhas 24-26) — o `FluentWindow` cuida do chrome nativamente:

```xml
<ui:FluentWindow x:Class="FiveMCleaner.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:primitives="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework"
        mc:Ignorable="d"
        Title="FiveMCleaner"
        Icon="Assets/FiveMCleaner.png"
        Width="1160"
        Height="680"
        MinWidth="960"
        MinHeight="580"
        WindowStartupLocation="CenterScreen"
        ExtendsContentIntoTitleBar="True"
        WindowBackdropType="Mica"
        ResizeMode="CanResize"
        FontFamily="Segoe UI Variable Text, Segoe UI"
        TextOptions.TextFormattingMode="Display"
        TextOptions.TextRenderingMode="ClearType"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource BackgroundBrush}">
```

E fechar com `</ui:FluentWindow>` no final do arquivo (era `</Window>`).

- [ ] **Step 2: Trocar a barra de título manual pelo `ui:TitleBar`**

Substituir o `<Border Grid.Row="0" ...>` de título (linhas 120-141, com
`Minimize_Click`/`Maximize_Click`/`Close_Click`) por `ui:TitleBar`, mantendo
logo/nome/badge como `Header`:

```xml
<ui:TitleBar Grid.Row="0" Height="54" Background="{DynamicResource ChromeBrush}" BorderBrush="{DynamicResource BorderSoftBrush}" BorderThickness="0,0,0,1">
    <ui:TitleBar.Header>
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="14,0,0,0">
            <Image Source="Assets/FiveMCleaner.png" Width="32" Height="32" Stretch="Uniform" />
            <TextBlock Margin="10,0,0,0" FontSize="15" FontWeight="Bold" VerticalAlignment="Center">
                <Run Text="FiveM" /><Run Text="Cleaner" Foreground="{DynamicResource OrangeBrush}" />
            </TextBlock>
            <Border Margin="12,0,0,0" MinHeight="26" Padding="9,0" Background="{DynamicResource ChipBrush}" CornerRadius="8" VerticalAlignment="Center">
                <TextBlock Text="{Binding EditionBadgeLabel}" FontSize="9" FontWeight="SemiBold" Foreground="{DynamicResource OrangeLightBrush}" VerticalAlignment="Center" />
            </Border>
        </StackPanel>
    </ui:TitleBar.Header>
</ui:TitleBar>
```

`ui:TitleBar` já fornece os botões nativos de minimizar/maximizar/fechar com
hover/press do sistema — os estilos `WindowButtonStyle`/`CloseWindowButtonStyle`
em `Controls.xaml` ficam sem uso nesta janela (mantidos no dicionário para as
3 janelas secundárias até a Task 8, se ainda os usarem; se nenhuma outra
janela usar, remover em Task 8).

- [ ] **Step 3: Remover os handlers de janela que não existem mais em `MainWindow.xaml.cs`**

Remover `Minimize_Click`, `Maximize_Click`, `Close_Click` e
`TitleBar_MouseLeftButtonDown` (linhas 278-282 e o handler de arrastar,
localizar via grep) — `ui:TitleBar` cuida de minimizar/maximizar/fechar/
arrastar nativamente. Se `MaximizeButton`/`MaximizeGlyph` (`x:Name`
referenciados em `MainWindow_StateChanged` ou similar) forem usados em outro
lugar do code-behind para trocar o glyph ao maximizar, localizar esse uso via
`grep -n "MaximizeButton\|MaximizeGlyph" src/FiveMCleaner.App/MainWindow.xaml.cs`
e remover também (o `ui:TitleBar` já troca o próprio ícone sozinho).

- [ ] **Step 4: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros. Se houver erro de referência a `x:Name` removido
(`MaximizeButton`, `MaximizeGlyph`, etc.) em outro trecho do `.xaml.cs`,
remover esse trecho também antes de re-buildar.

- [ ] **Step 5: Checagem manual**

Abrir via `scripts/Start-DevelopmentApp.ps1`. Confirmar: janela abre com
fundo Mica (leve translucidez ligada ao papel de parede), título/logo/badge
aparecem na barra, botões nativos de minimizar/maximizar/fechar funcionam,
arrastar pela barra de título move a janela, redimensionar pelas bordas
continua funcionando.

- [ ] **Step 6: Commit**

```bash
git add src/FiveMCleaner.App/MainWindow.xaml src/FiveMCleaner.App/MainWindow.xaml.cs
git commit -m "feat(app): migrate MainWindow to ui:FluentWindow with Mica backdrop"
```

---

### Task 4: Sidebar vira ui:NavigationView

**Files:**
- Modify: `src/FiveMCleaner.App/MainWindow.xaml:143-223` (bloco da sidebar)
- Modify: `src/FiveMCleaner.App/MainWindow.xaml.cs:386-408` (handlers de
  navegação)

**Interfaces:**
- Consumes: `Wpf.Ui.Controls.NavigationView`, `Wpf.Ui.Controls.NavigationViewItem`,
  `Wpf.Ui.Controls.SymbolIcon` (ou `Wpf.Ui.Controls.SymbolRegular` glyphs).
- Produces: `NavigationView_SelectionChanged(object, RoutedEventArgs)` novo
  handler que substitui `DashboardNav_Click`/`OptimizerNav_Click`/
  `HistoryNav_Click`/`SettingsNav_Click`, mas continua chamando o mesmo
  `Navigate(UIElement page, FrameworkElement navigation)` já existente
  (assinatura inalterada) — `ReviewPlan_Click`/`StartOptimization_Click`
  (linhas 394, 496) continuam chamando `Navigate(OptimizerPage, OptimizerNav)`
  sem mudança, então `OptimizerNav` precisa continuar existindo como
  `x:Name` referenciável (agora um `NavigationViewItem` em vez de `Button`).

- [ ] **Step 1: Substituir o `Border`/`StackPanel` da sidebar por `ui:NavigationView`**

Trocar o bloco `<Border Grid.Column="0" ...>` (linhas 149-223) por:

```xml
<ui:NavigationView x:Name="RootNavigationView"
                    Grid.Column="0"
                    IsBackButtonVisible="Collapsed"
                    IsSettingsVisible="False"
                    PaneDisplayMode="Left"
                    OpenPaneLength="210"
                    Background="{DynamicResource SidebarBrush}"
                    SelectionChanged="RootNavigationView_SelectionChanged">
    <ui:NavigationView.MenuItems>
        <ui:NavigationViewItem x:Name="DashboardNav" Content="{Binding [Navigation.Overview], Source={StaticResource LocalizedStrings}}" Icon="{ui:SymbolIcon Home24}" Tag="Dashboard" IsSelected="True" />
        <ui:NavigationViewItem x:Name="OptimizerNav" Content="{Binding [Navigation.Optimizer], Source={StaticResource LocalizedStrings}}" Icon="{ui:SymbolIcon FlashSettings24}" Tag="Optimizer" />
        <ui:NavigationViewItem x:Name="HistoryNav" Content="{Binding [Navigation.History], Source={StaticResource LocalizedStrings}}" Icon="{ui:SymbolIcon History24}" Tag="History" />
        <ui:NavigationViewItem x:Name="SettingsNav" Content="{Binding [Navigation.Settings], Source={StaticResource LocalizedStrings}}" Icon="{ui:SymbolIcon Settings24}" Tag="Settings" />
    </ui:NavigationView.MenuItems>
    <ui:NavigationView.FooterMenuItems>
        <ui:NavigationViewItem IsEnabled="False">
            <ui:NavigationViewItem.Content>
                <StackPanel Margin="0,4,0,4">
                    <Border Padding="13" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderSoftBrush}" BorderThickness="1" CornerRadius="11" ToolTip="{Binding [Safety.SnapshotRollback], Source={StaticResource LocalizedStrings}}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="30" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Border Width="25" Height="25" Background="#1C37C889" CornerRadius="7" VerticalAlignment="Top">
                                <TextBlock Text="&#xEA18;" FontFamily="Segoe MDL2 Assets" Foreground="{DynamicResource GreenBrush}" FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center" />
                            </Border>
                            <StackPanel Grid.Column="1" Margin="7,0,0,0" VerticalAlignment="Center">
                                <TextBlock Text="{Binding [Safety.Active], Source={StaticResource LocalizedStrings}}" FontSize="11.5" FontWeight="SemiBold" />
                            </StackPanel>
                        </Grid>
                    </Border>
                    <Border Margin="0,10,0,0" MinWidth="76" Padding="9,4,9,4" HorizontalAlignment="Center" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderSoftBrush}" BorderThickness="1" CornerRadius="7">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <TextBlock Text="{Binding [Sidebar.Version], Source={StaticResource LocalizedStrings}}" Foreground="{DynamicResource TextSubtleBrush}" FontFamily="Segoe UI Variable Text" FontSize="8" FontWeight="SemiBold" />
                            <Border Width="1" Height="10" Margin="7,0" Background="{DynamicResource BorderSoftBrush}" />
                            <TextBlock Text="{Binding AppVersion, Mode=OneWay}" Foreground="{DynamicResource TextBrush}" FontFamily="Segoe UI Variable Text" FontSize="9.5" FontWeight="SemiBold" />
                        </StackPanel>
                    </Border>
                </StackPanel>
            </ui:NavigationViewItem.Content>
        </ui:NavigationViewItem>
    </ui:NavigationView.FooterMenuItems>
</ui:NavigationView>
```

Nota: `ui:NavigationViewItem` herda de `Control`/`ButtonBase`, não de
`Button` — por isso o footer com o cartão de snapshot/versão (que não é um
item clicável de navegação) é embrulhado num item `IsEnabled="False"` só para
reaproveitar o layout do rodapé dentro do `FooterMenuItems` sem virar opção
de navegação real.

- [ ] **Step 2: Trocar os handlers de clique por um único `SelectionChanged`**

Em `MainWindow.xaml.cs`, remover `DashboardNav_Click`, `OptimizerNav_Click`,
`HistoryNav_Click`, `SettingsNav_Click` (linhas 386-392) e adicionar:

```csharp
private void RootNavigationView_SelectionChanged(object sender, RoutedEventArgs e)
{
    if (RootNavigationView.SelectedItem is not FrameworkElement { Tag: string tag })
    {
        return;
    }

    var (page, navigation) = tag switch
    {
        "Dashboard" => ((UIElement)DashboardPage, (FrameworkElement)DashboardNav),
        "Optimizer" => (OptimizerPage, OptimizerNav),
        "History" => (HistoryPage, HistoryNav),
        "Settings" => (SettingsPage, SettingsNav),
        _ => (DashboardPage, (FrameworkElement)DashboardNav)
    };
    Navigate(page, navigation);
}
```

`Navigate(UIElement page, FrameworkElement navigation)` (linha 396) continua
igual — mas o `navigation.Tag = "Selected"`/`= null` que ela fazia para pintar
o botão ativo não é mais necessário (o `NavigationViewItem` já pinta seleção
sozinho via `IsSelected`); simplificar `Navigate` removendo essas 8 linhas de
`Tag`:

```csharp
private void Navigate(UIElement page, FrameworkElement navigation)
{
    DashboardPage.Visibility = Visibility.Collapsed;
    OptimizerPage.Visibility = Visibility.Collapsed;
    HistoryPage.Visibility = Visibility.Collapsed;
    SettingsPage.Visibility = Visibility.Collapsed;
    page.Visibility = Visibility.Visible;
}
```

`ReviewPlan_Click`/`StartOptimization_Click` continuam chamando
`Navigate(OptimizerPage, OptimizerNav)` — para o item de navegação ficar
visualmente selecionado quando esses botões forem clicados, adicionar logo
antes da chamada a `Navigate`:

```csharp
RootNavigationView.SelectedItem = OptimizerNav;
```

(o que já dispara `RootNavigationView_SelectionChanged` sozinho, então a
chamada direta a `Navigate` nesses dois métodos pode ser removida — deixar
apenas `RootNavigationView.SelectedItem = OptimizerNav;`).

- [ ] **Step 3: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 4: Checagem manual**

Abrir via `scripts/Start-DevelopmentApp.ps1`. Clicar em cada item do menu
(Dashboard/Otimizador/Histórico/Configurações) e confirmar: a seção certa
aparece, o item selecionado fica destacado com o acento laranja, o painel
lateral com "Snapshot ativo"/versão continua visível no rodapé. Clicar em
"Revisar plano"/"Iniciar otimização" no Dashboard e confirmar que navega para
o Otimizador com o item de menu correspondente já destacado.

- [ ] **Step 5: Commit**

```bash
git add src/FiveMCleaner.App/MainWindow.xaml src/FiveMCleaner.App/MainWindow.xaml.cs
git commit -m "feat(app): replace manual sidebar with ui:NavigationView"
```

---

### Task 5: Cards migram para ui:CardControl

**Files:**
- Modify: `src/FiveMCleaner.App/MainWindow.xaml` (DataTemplates `ActionTemplate`
  linhas 29-45, `HistoryTemplate` linhas 89-109, e demais `Border
  Style="{StaticResource CardStyle}"` inline no corpo do Dashboard/Otimizador/
  Configurações — localizar todas com
  `grep -n 'Style="{StaticResource CardStyle}"' src/FiveMCleaner.App/MainWindow.xaml`)

**Interfaces:**
- Consumes: `Wpf.Ui.Controls.CardControl`, `Wpf.Ui.Controls.CardExpander`.
- Produces: nenhuma mudança de binding — `ActionTemplate`/`HistoryTemplate`
  continuam com as mesmas `Binding`s (`Name`, `Description`, `IconGlyph`,
  `Title`, `DateLabel`, `Summary`, `CanRollback`), só o container visual muda
  de `Border` para `ui:CardControl`.

- [ ] **Step 1: Migrar `ActionTemplate`**

```xml
<DataTemplate x:Key="ActionTemplate">
    <ui:CardControl Margin="0,0,0,9" Padding="14">
        <ui:CardControl.Icon>
            <Border Width="30" Height="30" CornerRadius="8" Background="#20FF7A18">
                <TextBlock Text="{Binding IconGlyph}" FontFamily="Segoe MDL2 Assets" FontSize="14" Foreground="{DynamicResource OrangeLightBrush}" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
        </ui:CardControl.Icon>
        <ui:CardControl.Header>
            <StackPanel>
                <TextBlock Text="{Binding Name}" FontSize="13" FontWeight="SemiBold" />
                <TextBlock Text="{Binding Description}" Margin="0,4,0,0" Foreground="{DynamicResource TextMutedBrush}" FontSize="11.5" TextWrapping="Wrap" />
            </StackPanel>
        </ui:CardControl.Header>
    </ui:CardControl>
</DataTemplate>
```

- [ ] **Step 2: Migrar `HistoryTemplate`**

```xml
<DataTemplate x:Key="HistoryTemplate">
    <ui:CardControl Margin="0,0,0,10" Padding="17">
        <ui:CardControl.Icon>
            <Border Width="38" Height="38" CornerRadius="10" Background="#1C37C889">
                <TextBlock Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" Foreground="{DynamicResource GreenBrush}" FontSize="16" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
        </ui:CardControl.Icon>
        <ui:CardControl.Header>
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="{Binding Title}" FontSize="13.5" FontWeight="SemiBold" />
                <TextBlock Margin="0,4,0,0" Foreground="{DynamicResource TextMutedBrush}" FontSize="11.5">
                    <Run Text="{Binding DateLabel, Mode=OneWay}" /><Run Text="  •  " /><Run Text="{Binding Summary, Mode=OneWay}" />
                </TextBlock>
            </StackPanel>
        </ui:CardControl.Header>
        <Button Content="{Binding [History.Undo], Source={StaticResource LocalizedStrings}}" Style="{StaticResource SecondaryButtonStyle}" Height="36" Padding="16,0" Tag="{Binding}" Click="RollbackHistory_Click" IsEnabled="{Binding CanRollback}" />
    </ui:CardControl>
</DataTemplate>
```

- [ ] **Step 3: Migrar os `Border Style="{StaticResource CardStyle}"` inline restantes**

Para cada ocorrência encontrada no Step "Files" acima (fora das duas
`DataTemplate`s já migradas), aplicar o mesmo padrão: trocar
`<Border Style="{StaticResource CardStyle}" ...>` por `<ui:CardControl ...>`
mantendo `Margin`/conteúdo interno idênticos (o `ui:CardControl` já tem
`CornerRadius`/`Background`/`BorderBrush` próprios do tema Fluent aplicado na
Task 2, então os atributos de `CardStyle` — `Background`, `BorderBrush`,
`BorderThickness`, `CornerRadius`, `Padding` — não precisam ser reescritos
manualmente, só remover o `Style="{StaticResource CardStyle}"` e usar a tag
`ui:CardControl`).

Se algum desses `Border` tiver `Padding` custom (ex.: `Padding="23"` no hero
com `HeroGradientBrush`, linha 310), preservar esse `Padding` explícito no
`ui:CardControl` correspondente, mas **não** migrar o hero
(`HeroGradientBrush`) para `ui:CardControl` — ele usa gradiente de fundo
customizado, fora do padrão simples de card; manter como `Border
Style="{StaticResource CardStyle}"` com o `Background` sobrescrito, como já
está hoje.

- [ ] **Step 4: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 5: Checagem manual**

Percorrer Dashboard, Otimizador, Histórico e Configurações. Confirmar que
todos os cards de ação, histórico e listas continuam com o conteúdo e
bindings corretos (nomes, descrições, datas, botão desfazer funcionando),
agora com a aparência de `CardControl` do WPF-UI (cantos, elevação, hover
padrão da lib).

- [ ] **Step 6: Commit**

```bash
git add src/FiveMCleaner.App/MainWindow.xaml
git commit -m "feat(app): migrate action/history cards to ui:CardControl"
```

---

### Task 6: Motion — hover/press em cards e botão primário

**Files:**
- Modify: `src/FiveMCleaner.App/Themes/Controls.xaml` (estilo
  `PrimaryButtonStyle`, linhas 101-132)
- Modify: `src/FiveMCleaner.App/Themes/Palette.xaml` (novo brush de glow)

**Interfaces:**
- Produces: `PrimaryButtonStyle` ganha um `DropShadowEffect` animado no hover;
  `ui:CardControl` (Task 5) ganha um `Style` próprio com `RenderTransform`
  animado no hover. Nenhuma API pública nova — só efeito visual.

- [ ] **Step 1: Adicionar o brush de glow em `Palette.xaml`**

Adicionar logo após `ColorOrangeDark` (linha 14):

```xml
<Color x:Key="ColorOrangeGlow">#FF7A18</Color>
```

- [ ] **Step 2: Adicionar animação de glow ao `PrimaryButtonStyle` em `Controls.xaml`**

Dentro do `ControlTemplate` de `PrimaryButtonStyle` (linhas 113-116), dar um
`x:Name` ao `Border` já existente (já tem `x:Name="Root"`) e anexar um
`DropShadowEffect` inicial + trigger animado:

```xml
<Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="9">
    <Border.Effect>
        <DropShadowEffect x:Name="RootGlow" Color="{StaticResource ColorOrangeGlow}" BlurRadius="0" ShadowDepth="0" Opacity="0.8" />
    </Border.Effect>
    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
</Border>
```

E substituir o trigger de `IsMouseOver` existente (linhas 118-120) para além
de baixar a opacidade, animar o `BlurRadius` do glow:

```xml
<Trigger Property="IsMouseOver" Value="True">
    <Setter TargetName="Root" Property="Opacity" Value="0.9" />
    <Trigger.EnterActions>
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetName="RootGlow" Storyboard.TargetProperty="BlurRadius" To="18" Duration="0:0:0.15" />
            </Storyboard>
        </BeginStoryboard>
    </Trigger.EnterActions>
    <Trigger.ExitActions>
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetName="RootGlow" Storyboard.TargetProperty="BlurRadius" To="0" Duration="0:0:0.15" />
            </Storyboard>
        </BeginStoryboard>
    </Trigger.ExitActions>
</Trigger>
```

Manter o trigger de `IsPressed` (linhas 121-123) e `IsEnabled=False` (linhas
124-127) exatamente como estão hoje, apenas movidos para depois deste bloco
editado (a ordem dos `Trigger`s dentro de `ControlTemplate.Triggers` não
afeta comportamento).

- [ ] **Step 3: Adicionar hover de elevação em `ui:CardControl` via `Style` global**

Em `Controls.xaml`, adicionar um `Style` sem `x:Key` (aplica a todo
`CardControl` do app) logo após `CardStyle` (linha 40):

```xml
<Style TargetType="ui:CardControl" xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Setter Property="RenderTransformOrigin" Value="0.5,0.5" />
    <Setter Property="RenderTransform">
        <Setter.Value>
            <ScaleTransform ScaleX="1" ScaleY="1" />
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Trigger.EnterActions>
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleX" To="1.02" Duration="0:0:0.15" />
                        <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleY" To="1.02" Duration="0:0:0.15" />
                    </Storyboard>
                </BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleX" To="1" Duration="0:0:0.15" />
                        <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleY" To="1" Duration="0:0:0.15" />
                    </Storyboard>
                </BeginStoryboard>
            </Trigger.ExitActions>
        </Trigger>
    </Style.Triggers>
</Style>
```

(O atributo `xmlns:ui` inline no `<Style>` é redundante se `Controls.xaml` já
declarar `xmlns:ui` no elemento raiz `ResourceDictionary` — checar isso ao
editar e usar a declaração já existente no topo do arquivo em vez de repetir
inline; se `Controls.xaml` ainda não declarar `ui`, adicionar
`xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"` no `<ResourceDictionary>`
raiz do arquivo, linha 1-2, e remover do `<Style>`.)

- [ ] **Step 4: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 5: Checagem manual**

Passar o mouse sobre o botão "Iniciar otimização" (ou qualquer
`PrimaryButtonStyle`) e confirmar o glow laranja crescendo suavemente. Passar
o mouse sobre um card de ação/histórico e confirmar a leve elevação
(scale ~1.02) com transição suave, sem tremor ou clipping visual.

- [ ] **Step 6: Commit**

```bash
git add src/FiveMCleaner.App/Themes/Controls.xaml src/FiveMCleaner.App/Themes/Palette.xaml
git commit -m "feat(app): add hover glow/elevation motion to primary button and cards"
```

---

### Task 7: Barra de progresso do Otimizador com gradiente animado

**Files:**
- Modify: `src/FiveMCleaner.App/MainWindow.xaml` (bloco da linha do tempo
  compacta do Otimizador — localizar via
  `grep -n "ProgressBar" src/FiveMCleaner.App/MainWindow.xaml` dentro da
  seção `OptimizerPage`)

**Interfaces:**
- Consumes: `IsOptimizationRunning` (ou binding equivalente já existente no
  `MainViewModel` que controla a visibilidade/estado da barra durante a
  execução — confirmar o nome exato lendo o binding atual do `ProgressBar` do
  Otimizador antes de editar).
- Produces: nenhum binding novo — só um `Style` local com animação
  condicionada ao mesmo `Visibility`/`IsEnabled` que a barra já usa.

- [ ] **Step 1: Ler o bloco atual da barra de progresso do Otimizador**

Antes de editar, ler as ~20 linhas ao redor do `ProgressBar` da linha do
tempo compacta (dentro de `OptimizerPage`, não o `ProgressBar` de download de
update já visto na linha 298) para confirmar o `Binding` de `Value` e o
`Binding`/condição que indica "rodando" (ex.: `IsOptimizationRunning`,
`IsBusy` — usar o nome real encontrado).

- [ ] **Step 2: Adicionar gradiente animado ao indicador enquanto roda**

Envolver o `ProgressBar` existente (mantendo seu `Value`/`Minimum`/`Maximum`
bindings intactos) com um `Style` local que troca o `Foreground` para um
`LinearGradientBrush` animado apenas enquanto a otimização está em execução:

```xml
<ProgressBar Minimum="0" Maximum="100" Value="{Binding OptimizationProgressPercent, Mode=OneWay}">
    <ProgressBar.Style>
        <Style TargetType="ProgressBar">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsOptimizationRunning}" Value="True">
                    <Setter Property="Foreground">
                        <Setter.Value>
                            <LinearGradientBrush x:Name="RunningGradient" StartPoint="0,0" EndPoint="1,0">
                                <GradientStop Color="#FF7A18" Offset="0" />
                                <GradientStop Color="#FFAA62" Offset="0.5" />
                                <GradientStop Color="#FF7A18" Offset="1" />
                            </LinearGradientBrush>
                        </Setter.Value>
                    </Setter>
                    <DataTrigger.EnterActions>
                        <BeginStoryboard>
                            <Storyboard RepeatBehavior="Forever">
                                <DoubleAnimation Storyboard.TargetProperty="(ProgressBar.Foreground).(LinearGradientBrush.Transform).(TranslateTransform.X)" From="-1" To="1" Duration="0:0:1.4" />
                            </Storyboard>
                        </BeginStoryboard>
                    </DataTrigger.EnterActions>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ProgressBar.Style>
</ProgressBar>
```

Nota: animar `Transform` de um `LinearGradientBrush` exige que o brush tenha
um `RelativeTransform`/`Transform` declarado; ajustar o XAML acima para
declarar explicitamente:

```xml
<LinearGradientBrush x:Name="RunningGradient" StartPoint="0,0" EndPoint="1,0">
    <LinearGradientBrush.Transform>
        <TranslateTransform x:Name="RunningGradientTransform" X="0" />
    </LinearGradientBrush.Transform>
    <GradientStop Color="#FF7A18" Offset="0" />
    <GradientStop Color="#FFAA62" Offset="0.5" />
    <GradientStop Color="#FF7A18" Offset="1" />
</LinearGradientBrush>
```

e trocar o alvo da animação para
`Storyboard.TargetName="RunningGradientTransform"` /
`Storyboard.TargetProperty="X"` (mais simples e confiável que animar dentro
de `(ProgressBar.Foreground)`).

- [ ] **Step 3: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 4: Checagem manual**

Iniciar uma otimização real (ou pelo atalho de desenvolvimento) e observar a
barra de progresso: enquanto roda, o preenchimento deve mostrar um brilho se
deslocando da esquerda pra direita em loop; ao terminar, volta ao
`OrangeBrush` sólido estático já existente.

- [ ] **Step 5: Commit**

```bash
git add src/FiveMCleaner.App/MainWindow.xaml
git commit -m "feat(app): animate optimizer progress bar gradient while running"
```

---

### Task 8: Janelas secundárias viram FluentWindow

**Files:**
- Modify: `src/FiveMCleaner.App/Views/OptimizationConfirmationWindow.xaml`
- Modify: `src/FiveMCleaner.App/Views/BugReportWindow.xaml`
- Modify: `src/FiveMCleaner.App/Views/PrivacyConsentWindow.xaml`
- Modify: os respectivos `.xaml.cs` de cada janela acima (mesmo tipo de
  ajuste de chrome/handlers feito na Task 3 para `MainWindow`)

**Interfaces:**
- Consumes: mesmo `Wpf.Ui.Controls.FluentWindow`/`ui:TitleBar` da Task 3.
- Produces: nenhuma mudança de `DataContext`/binding — só elemento raiz e
  chrome.

- [ ] **Step 1: Ler as três janelas por completo antes de editar**

Cada uma tem layout próprio e pode ou não ter `WindowChrome`/título manual
(`PrivacyConsentWindow` é sabidamente sem botão de fechar, por design — ver
`PROJECT_STATE.md`, "Consentimento de privacidade obrigatório"; **não**
adicionar um `ui:TitleBar` com botão de fechar visível/funcional nela — ao
migrar essa janela para `ui:FluentWindow`, omitir completamente o
`ui:TitleBar` ou usar `ui:TitleBar` com
`Icon="{x:Null}"` e sem o botão de fechar habilitado, preservando o
comportamento atual de bloquear o fechamento).

- [ ] **Step 2: Aplicar o mesmo padrão da Task 3 (Steps 1-2) a cada janela**

Para `OptimizationConfirmationWindow.xaml` e `BugReportWindow.xaml`: trocar
elemento raiz `Window` → `ui:FluentWindow`, com
`WindowBackdropType="Mica"`, `ExtendsContentIntoTitleBar="True"`, e um
`ui:TitleBar` simples com apenas o título da janela (sem o logo/badge do
`MainWindow`, que é exclusivo dela):

```xml
<ui:TitleBar Grid.Row="0" Title="{Binding Title, RelativeSource={RelativeSource AncestorType=Window}}" />
```

Para `PrivacyConsentWindow.xaml`: trocar elemento raiz para `ui:FluentWindow`
com `WindowBackdropType="Mica"`, mas **sem** `ui:TitleBar` (manter o
comportamento de não ter botão de fechar/minimizar/maximizar, consistente
com o bloqueio de `Alt+F4` já implementado no code-behind — confirmar que o
handler de `Closing`/`PreventClose` existente continua registrado e
funcionando após a troca de classe base).

- [ ] **Step 3: Ajustar os `.xaml.cs` de cada janela**

Igual à Task 3 Step 3: remover qualquer handler de `Minimize_Click`/
`Maximize_Click`/`Close_Click`/arrastar manual que essas janelas tiverem
duplicado do `MainWindow` (confirmar caso a caso — pode ser que alguma já
não tenha, por ser modal sem esses botões).

- [ ] **Step 4: Reaproveitar `CardStyle`/`ui:CardControl` e `PrimaryButtonStyle`/`SecondaryButtonStyle` já migrados**

Se alguma das três janelas usar `Border Style="{StaticResource CardStyle}"`
internamente, aplicar o mesmo Step 3 da Task 5 (trocar para `ui:CardControl`)
para consistência visual com a janela principal.

- [ ] **Step 5: Build para verificar**

Run: `dotnet build src/FiveMCleaner.App/FiveMCleaner.App.csproj`
Expected: build sem erros.

- [ ] **Step 6: Checagem manual de cada janela**

- `OptimizationConfirmationWindow`: disparar a prévia de otimização e
  confirmar que a janela abre com Mica, título correto, e todos os botões
  (confirmar/cancelar) funcionam.
- `BugReportWindow`: abrir pelo Dashboard/Configurações e confirmar envio de
  relato continua funcionando, com o novo chrome.
- `PrivacyConsentWindow`: confirmar que **continua sem** botão de fechar
  visível, que `Alt+F4` continua sendo bloqueado até escolher os seletores e
  clicar "Continuar", exatamente como documentado em `PROJECT_STATE.md`.

- [ ] **Step 7: Commit**

```bash
git add src/FiveMCleaner.App/Views/OptimizationConfirmationWindow.xaml src/FiveMCleaner.App/Views/OptimizationConfirmationWindow.xaml.cs src/FiveMCleaner.App/Views/BugReportWindow.xaml src/FiveMCleaner.App/Views/BugReportWindow.xaml.cs src/FiveMCleaner.App/Views/PrivacyConsentWindow.xaml src/FiveMCleaner.App/Views/PrivacyConsentWindow.xaml.cs
git commit -m "feat(app): migrate secondary windows to ui:FluentWindow"
```

---

### Task 9: Validação final completa

**Files:** nenhum arquivo novo — apenas validação.

- [ ] **Step 1: Build Release completo**

Run: `dotnet build FiveMCleaner.slnx -c Release`
Expected: build sem erros nem avisos novos (comparar com o baseline antes
desta rodada, registrado em `PROJECT_STATE.md`).

- [ ] **Step 2: Suíte de testes .NET completa**

Run: `dotnet test FiveMCleaner.slnx -c Release`
Expected: mesma contagem de testes de antes desta rodada, todos passando
(nenhuma tarefa deste plano tocou `ViewModels`/lógica, então nenhum teste
deveria ter sido afetado; se algum falhar, investigar antes de prosseguir —
não é esperado).

- [ ] **Step 3: Safety check**

Run: `powershell -File scripts/Verify-Safety.ps1`
Expected: aprovado (nenhuma mudança desta rodada tocou fora de
`FiveMCleaner.App/Themes`, `/Views`, `/Services/ThemeManager.cs`,
`/MainWindow.xaml*`).

- [ ] **Step 4: Passagem visual manual completa, tema escuro**

Via `scripts/Start-DevelopmentApp.ps1`: percorrer Dashboard → Otimizador
(rodar uma otimização completa) → Histórico → Configurações; abrir
`BugReportWindow` e `OptimizationConfirmationWindow`. Confirmar Mica,
NavigationView, cards, glow de botão e gradiente de progresso, todos
coerentes visualmente.

- [ ] **Step 5: Passagem visual manual completa, tema claro**

Repetir o Step 4 depois de trocar para tema Claro em Configurações.
Confirmar que o accent laranja (`#E85D04` claro) e todos os brushes de
`LightPalette` em `ThemeManager.cs` continuam legíveis/coerentes com Mica no
tema claro (contraste de texto, bordas visíveis).

- [ ] **Step 6: Atualizar `PROJECT_STATE.md`**

Adicionar uma entrada nova no topo do arquivo (mesmo padrão das entradas
existentes) resumindo: adoção do WPF-UI, `FluentWindow`/Mica,
`NavigationView` (com a nota do desvio de arquitetura documentado no início
deste plano), `CardControl`, motion de hover/glow/progresso, migração das 3
janelas secundárias, e o resultado da validação (build/testes/safety).

- [ ] **Step 7: Commit final**

```bash
git add PROJECT_STATE.md
git commit -m "docs: record full Fluent Design (WPF-UI) visual redesign in project state"
```
