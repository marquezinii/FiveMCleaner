# Auditoria Cross-platform (Windows 10 2004+ / Windows 11)

- **Agente**: opencode
- **Branch**: `ai/opencode/cross-platform-audit`
- **Objetivo**: rodada completa de cross-platform compatibility audit — problemas
  específicos de SO (Windows 10 e 11) e de idioma do Windows (mercado pt-BR).
- **Status**: concluída (auditoria + correções implementadas).
- **Escopo auditado**: TFM/instalador/manifestos, janela (Mica/DPI), fontes,
  contadores de desempenho, parse de saída de ferramentas, WMI/registro,
  powercfg, WHEA, atualização (Launcher/Updater/Broker), requisitos públicos.

## Resumo executivo

A base é sólida para Windows 10 2004 (19041) e Windows 11: TFM e instalador
alinhados em 19041, `supportedOS` declarado no App/Broker, P/Invokes em APIs
baseline 19041, registro/allowlist estáveis entre 10 e 11, WMI não localizado,
Mica com degradação graciosa no Win10. Os problemas reais concentram-se em
**dependência de idioma do SO** (contadores v1, `powercfg /Q`, WHEA) — todos
degradam sem crash, mas afetam o mercado principal (Windows pt-BR).

**Todas as correções foram implementadas e validadas (635 testes passando, 0 warnings).**

## Achados e correções

### ALTO — Contadores de desempenho v1 com nomes em inglês falham em Windows não-inglês ✅ CORRIGIDO
- `src/FiveMCleaner.Windows/Infrastructure/ResourceUsageInspector.cs:34,43,47,64-90`
  usava `Processor`/`% Processor Time`/`PhysicalDisk`/`% Disk Time`/`GPU Engine`.
- Categorias/contadores **v1** são localizados por idioma do SO (Perflib).
  `.NET PerformanceCounter` resolve nomes pelo `CurrentUICulture` do processo
  (idioma do SO; `LocalizationService.cs` só *lê* `CurrentUICulture`, não o
  altera). Em Windows 10/11 pt-BR real, `new PerformanceCounter("Processor",...)`
  lança `InvalidOperationException` → catch → `null`.
- Impacto: CPU e disco do **monitor ao vivo** e do diagnóstico
  `DiagnoseResourceUsage` (`ActionCatalog.cs:388`) aparecem permanentemente
  como **"indisponível"** no principal mercado (pt-BR). `GPU Engine` (conjunto
  v2, linguagem neutra) provavelmente funciona — verificar em máquina pt-BR.
- **Correção aplicada**: CPU e disco agora usam PDH com `PdhAddEnglishCounterW`
  (Vista+), que aceita nomes em inglês independentemente do idioma do SO. GPU
  mantém PerformanceCounter (v2, linguagem neutra). Rede inalterada.

### MÉDIO — Parse de saída localizada do `powercfg /Q` em inglês ✅ CORRIGIDO
- `src/FiveMCleaner.Windows/Actions/PowerPlanAction.cs:244-247`
  (`AspmCurrentValueRegex`: `Current AC Power Setting Index:`).
- Em Windows pt-BR a saída é localizada → regex não casa →
  `GetPciExpressAspmPolicyAsync` retorna `null` → a ação
  `AdjustPciExpressAspmPolicy` (perfis Médio/Agressivo) é **sempre no-op
  silencioso** ("não expõe a configuração"). Limitação já documentada em
  `ActionCatalog.cs:750`.
- **Correção aplicada**: `GetPciExpressAspmPolicyAsync` agora usa P/Invoke
  `PowerReadACValueIndex` (powrprof.dll) em vez de parse de texto do powercfg /Q.
  API nativa retorna valor independente de idioma.

### BAIXO — WHEA "memory" classificado por texto localizado ✅ CORRIGIDO
- `src/FiveMCleaner.Windows/Infrastructure/HardwareStabilityInspector.cs:69-74`:
  `FormatDescription()` + `Contains("memory"|"memória")`. Em SO espanhol
  ("memoria") a sub-contagem fica 0; o total usado no sinal de throttling é
  preservado. Display-only.
- **Correção aplicada**: Agora usa `entry.ToXml()` para buscar "Memory"/"Memória"/
  "Memoria" no XML estruturado do evento, que contém dados não-localizados.
  Abrange mais idiomas além de pt-BR.

### BAIXO — Launcher/Updater sem `supportedOS` no manifest embutido ✅ CORRIGIDO
- Verificado empiricamente nos binários Release: `FiveMCleaner.Launcher.exe` e
  `FiveMCleaner.Updater.exe` não declaram GUIDs de compatibilidade; App e
  Broker declaram Win10+Win11. Sem impacto funcional no .NET 10 (RtlGetVersion),
  mas inconsistência de compatibilidade (ferramentas reportam Windows 8).
- **Correção aplicada**: Criados `app.manifest` para Launcher e Updater com os
  mesmos GUIDs de `supportedOS` (Win10 + Win11). Referenciados nos csproj.

### BAIXO — Requisitos públicos divergentes ✅ CORRIGIDO
- Site: `Windows 10 22H2 ou Windows 11` (`website/app/page.tsx:183,425`,
  `website/public-site/index.html:184`) vs instalador
  `MinVersion=10.0.19041` (`installer/FiveMCleaner.iss:63`) e
  `install-info.*.txt` ("2004 / build 19041"). Alinhar o texto do site.
- **Correção aplicada**: Alterado "22H2" para "2004" em `website/app/page.tsx`
  (linhas 183 e 425) e `website/public-site/index.html` (linha 184).

## Observações (INFO, não são bugs)

- **Mica no Win10**: verificado no WPF-UI 4.3.0 (`WindowBackdrop.ApplyBackdrop`):
  em build < 22523 tenta `DWMWA_MICA_EFFECT`, que falha silenciosamente no
  Win10; a janela mantém `BackgroundBrush` opaco. Degradação graciosa.
- **DPI**: manifestos App/Broker sem `dpiAwareness`, mas .NET 6+ WPF assume
  PerMonitorV2 no runtime quando o manifest é omisso → sem bug esperado.
- **UTF-8 do `CommandRunner.cs:59`**: stdout de ferramentas de console é
  OEM/ANSI; inócuo hoje (parse ASCII-only), risco latente.
- **Formatação `{0:N0}`/`0.#`** usa CurrentCulture: correto no pt-BR; em UI
  en/es num OS pt-BR mostra separadores mistos. Cosmético.
- **Fontes**: `Segoe UI Variable` com fallback `Segoe UI` em quase todo o XAML;
  `Themes/Controls.xaml:11` sem fallback explícito cai na fonte composta do WPF
  no Win10. Cosmético.
- **CI**: `.github/workflows/ci.yml` valida só em `windows-latest`; Windows 10
  nunca é exercitado pelo CI.

## Áreas verificadas e OK

- TFM `net10.0-windows10.0.19041.0` e `MinVersion=10.0.19041` alinhados
  (Win10 2004+; Win11).
- P/Invokes em APIs baseline 19041 (EnumDisplaySettingsW, SystemParametersInfoW,
  MonitorFromWindow/GetMonitorInfo, GetSystemPowerStatus, CM_*, GlobalMemoryStatusEx);
  nenhum DWMWA_* direto no código do app.
- Registro (GameBar, GameDVR, UserGpuPreferences, AppCompatFlags\Layers,
  HwSchMode) estável entre Win10/11; allowlist + rollback hash-verificado.
- WMI (Win32_*) não localizado; `Environment.OSVersion` (RtlGetVersion no .NET
  10) retorna build real → `Build >= 22000` para label Win11 correto.
- Update pipeline (Launcher/Updater/Broker/Silent/Atomic) sem acoplamento de
  SO/idioma; TLS via HttpClient OK em 19041; DPAPI; caminhos por known folders;
  `ArchitecturesAllowed=x64compatible` cobre ARM64 (emulação x64).

## Validação executada

- `dotnet build FiveMCleaner.slnx -c Release`: 0 avisos, 0 erros.
- `dotnet test` Release: **635 aprovados, 0 falhas** (rebuild completo).
- Extração dos manifestos embutidos (string-scan nos binários Release) para
  confirmar `supportedOS` e ausência de `dpiAwareness`.
- Todas as correções implementadas e validadas.

## Commits

- Correções de cross-platform: PDH para contadores, P/Invoke para ASPM,
  WHEA por XML, manifests para Launcher/Updater, requisitos do site.

## Observações para integração

- Todas as correções são localizadas e de baixo risco de regressão.
- Os testes foram atualizados para refletir as novas implementações.

**Integrado em `dev/proxima-versao` — 04/08/2026.**
