# Auditoria e Expansão de Códigos de Erro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every user-visible optimization failure (overall run and per action) shows a stable `BugCode` next to its message; the enum gains real, justified new codes for two currently-uncovered app surfaces; a lightweight category catalog and the bug-report/dashboard views get a human-readable category instead of a raw enum string.

**Architecture:** `BugCodeClassifier` moves from `Ralven.App.Services` to `Ralven.Windows.Diagnostics` so `WindowsTransactionEngine` (which owns the real exception at the moment of failure) can classify a `BugCode` per action and store it on the journal entry, from which `OptimizationReportBuilder` propagates it to the report DTO the UI already renders. A new `BugCodeCatalog.GetCategory(BugCode)` mechanically derives a category (the prefix before the first `_`) so every current and future code — not just the ones added here — resolves to a translated category label with zero per-code translation debt.

**Tech Stack:** .NET 10 / C# 14, xUnit v3 (Ralven.Tests), WPF/MVVM (Ralven.App), Cloudflare Worker (Node/vitest or node:test — follow existing `infra/cloudflare-worker/test` runner), static dashboard (node:test, `infra/dashboard/test`).

**Spec:** `docs/superpowers/specs/2026-09-03-error-codes-audit-expand-design.md`

## Global Constraints

- `BugCode` is an append-only durable contract: never rename, remove, or renumber an existing member (see doc comment in `src/Ralven.Contracts/BugCode.cs`).
- `Ralven.Windows` may depend on `Ralven.Contracts` and `Ralven.Core` only — never on `Ralven.App` (architectural boundary in `docs/architecture.md`). This is why `BugCodeClassifier` must move, not just be called from a new place.
- No new BugCode may be added without a real, already-existing call site that can set it in this same plan — no speculative codes (this plan deliberately does **not** add a billing category: `Ralven.App` has no checkout-initiating code yet, only the Worker/D1 foundation, so there is nothing to classify).
- Every string shown to the user goes through the existing `ILocalizationService` (`GetString`/`Format`) and exists in all three resx files: `Strings.resx` (English/neutral), `Strings.pt-BR.resx`, `Strings.es.resx`.
- Dashboard (`infra/dashboard`) is Portuguese-only with no i18n system — new label text there is pt-BR literal, not localized.
- Don't touch `infra/dashboard/assets/rendering.js` internals (interactive chart engine) — an unintegrated branch (`feat/dashboard-insights`) rewrites it and this plan must not create unnecessary merge conflicts with it.

---

### Task 1: Relocate `BugCodeClassifier` to `Ralven.Windows` and fix its context fallthrough

**Files:**
- Create: `src/Ralven.Windows/Diagnostics/BugCodeClassifier.cs` (moved content)
- Delete: `src/Ralven.App/Services/BugCodeClassifier.cs`
- Modify: `src/Ralven.Launcher/Program.cs`, `src/Ralven.App/ViewModels/MainViewModel.Optimization.cs`, `src/Ralven.App/Services/SignedManifestUpdateService.cs`, `src/Ralven.App/Services/AtomicUpdateInstaller.cs` (namespace `using`)
- Test: `tests/Ralven.Tests/Windows/BugCodeClassifierTests.cs` (new — the class had no direct unit tests before this move)

**Interfaces:**
- Produces: `Ralven.Windows.Diagnostics.BugCodeClassifier` with the same public API as before: `ClassifyException(Exception, string? context = null)`, `ClassifyOptimizationException(Exception, string? actionId = null)`, `ClassifyUpdaterException(Exception, string? stage = null)`, `ClassifyBrokerException(Exception, string? actionId = null)`, all returning `Ralven.Contracts.BugCode`.

- [ ] **Step 1: Write the failing test for the fallthrough bug**

`ClassifyException`'s generic fallthrough currently always returns `BugCode.APP_OPT_ACTION_EXECUTION`, even when `context` is a non-optimization value such as `"app-inventory"`. Write the test first, against the *current* location, so it fails for the right reason:

```csharp
using Ralven.Contracts;
using Ralven.Windows.Diagnostics;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class BugCodeClassifierTests
{
    [Fact]
    public void ClassifyException_UnrecognizedExceptionType_FallsBackToOptimizationOnlyWhenContextIsOptimization()
    {
        var result = BugCodeClassifier.ClassifyException(new FormatException("boom"), "optimization");

        Assert.Equal(BugCode.APP_OPT_ACTION_EXECUTION, result);
    }

    [Fact]
    public void ClassifyException_UnrecognizedContext_ReturnsUnknownRatherThanOptimization()
    {
        var result = BugCodeClassifier.ClassifyException(new FormatException("boom"), "some-unmapped-context");

        Assert.Equal(BugCode.Unknown, result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeClassifierTests"`
Expected: build error (namespace `Ralven.Windows.Diagnostics` does not exist yet) — this is expected at this point; proceed to Step 3.

- [ ] **Step 3: Move the file and fix the fallthrough**

Move `src/Ralven.App/Services/BugCodeClassifier.cs` to `src/Ralven.Windows/Diagnostics/BugCodeClassifier.cs`, changing only:
- `namespace Ralven.App.Services;` → `namespace Ralven.Windows.Diagnostics;`
- The `ClassifyException` method's final line, from:

```csharp
            // Generic fallthrough
            _ => BugCode.APP_OPT_ACTION_EXECUTION
        };
    }
```

to:

```csharp
            // Generic fallthrough: only assume "optimization" when nothing
            // more specific matched and the caller actually said so; an
            // unrecognized context must not silently look like an
            // optimization failure.
            _ => context switch
            {
                "optimization" => BugCode.APP_OPT_ACTION_EXECUTION,
                _ => BugCode.Unknown
            }
        };
    }
```

Delete the original `src/Ralven.App/Services/BugCodeClassifier.cs`.

- [ ] **Step 4: Update the four callers' `using` statements**

In each of `src/Ralven.Launcher/Program.cs`, `src/Ralven.App/ViewModels/MainViewModel.Optimization.cs`, `src/Ralven.App/Services/SignedManifestUpdateService.cs`, `src/Ralven.App/Services/AtomicUpdateInstaller.cs`: add `using Ralven.Windows.Diagnostics;` alongside the existing `using Ralven.Contracts;` (do not remove `Ralven.App.Services` if the file still uses other types from it).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeClassifierTests"`
Expected: PASS (2/2).

- [ ] **Step 6: Full build to confirm no leftover reference to the old namespace**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Ralven.Windows/Diagnostics/BugCodeClassifier.cs src/Ralven.App/Services/BugCodeClassifier.cs src/Ralven.Launcher/Program.cs src/Ralven.App/ViewModels/MainViewModel.Optimization.cs src/Ralven.App/Services/SignedManifestUpdateService.cs src/Ralven.App/Services/AtomicUpdateInstaller.cs tests/Ralven.Tests/Windows/BugCodeClassifierTests.cs
git commit -m "refactor(windows): move BugCodeClassifier into Ralven.Windows

Ralven.Windows cannot depend on Ralven.App, but per-action failure
classification (next task) needs the classifier from inside
WindowsTransactionEngine. Also fixes ClassifyException always
defaulting unmatched exceptions to an optimization code regardless
of the caller's context."
```

---

### Task 2: Expand `BugCode` with the two justified new values

**Files:**
- Modify: `src/Ralven.Contracts/BugCode.cs`
- Modify: `src/Ralven.Windows/Diagnostics/BugCodeClassifier.cs`
- Test: `tests/Ralven.Tests/Windows/BugCodeClassifierTests.cs`

**Interfaces:**
- Produces: `BugCode.APP_INV_SCAN = 407`, `BugCode.SEC_HEALTH_QUERY = 1300`.
- Produces: `BugCodeClassifier.ClassifyException(ex, "app-inventory")` and `ClassifyException(ex, "security-health")` branches.

**Justification (from the audit):** `WindowsApplicationInventoryInspector`'s consumer (`ApplicationsPageViewModel.RefreshAsync`) and `WindowsSystemHealthInspector.Read` both already catch a real exception and fall back to an "unavailable" state/message with **no** BugCode today — real, existing failure surfaces with zero classification.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ClassifyException_AppInventoryContext_ReturnsAppInventoryScan()
    {
        var result = BugCodeClassifier.ClassifyException(new UnauthorizedAccessException(), "app-inventory");

        Assert.Equal(BugCode.APP_INV_SCAN, result);
    }

    [Fact]
    public void ClassifyException_SecurityHealthContext_ReturnsSecurityHealthQuery()
    {
        var result = BugCodeClassifier.ClassifyException(new DllNotFoundException(), "security-health");

        Assert.Equal(BugCode.SEC_HEALTH_QUERY, result);
    }
```

Add these to `tests/Ralven.Tests/Windows/BugCodeClassifierTests.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeClassifierTests"`
Expected: the two new tests FAIL (enum members don't exist yet / classifier doesn't route them yet).

- [ ] **Step 3: Add the enum members**

In `src/Ralven.Contracts/BugCode.cs`, after `APP_TRAY_SERVICE = 406,` (still inside the `APP_` region) add:

```csharp

    /// <summary>Aplicativos/Startup inventory scan failed (partial or total).</summary>
    APP_INV_SCAN = 407,
```

After the `WIN_BIOS = 1011,` block (end of the `WIN_` region, before the `CFG_` region starts), add a new region:

```csharp

    // ========== SECURITY HEALTH (SEC_) ==========

    /// <summary>Windows Security Center health read (antivirus, firewall, updates) failed.</summary>
    SEC_HEALTH_QUERY = 1300,
```

Also update the class-level `<remarks>` doc comment at the top of the file to add two lines to the prefix list:

```
/// - SEC_: Windows Security Center health (antivirus, firewall, automatic updates)
```

(placed after the `- SYS_:` line).

- [ ] **Step 4: Route the two new contexts in `ClassifyException`**

`UnauthorizedAccessException` already has a context switch (`ClassifyUnauthorizedAccessException`). Add a case there:

```csharp
    private static BugCode ClassifyUnauthorizedAccessException(UnauthorizedAccessException ex, string? context)
    {
        return context switch
        {
            "updater" => BugCode.UPD_INSTALLER_EXECUTION,
            "broker" => BugCode.BRK_UAC_DENIED,
            "registry" => BugCode.WIN_REGISTRY,
            "service" => BugCode.WIN_SERVICE,
            "power" => BugCode.WIN_POWER_PLAN,
            "app-inventory" => BugCode.APP_INV_SCAN,
            _ => BugCode.WIN_PRIVILEGE
        };
    }
```

The generic (non-typed) fallthrough fixed in Task 1 also needs both new contexts, since inventory/health failures can be any exception type, not just `UnauthorizedAccessException`:

```csharp
            // Generic fallthrough: only assume "optimization" when nothing
            // more specific matched and the caller actually said so; an
            // unrecognized context must not silently look like an
            // optimization failure.
            _ => context switch
            {
                "optimization" => BugCode.APP_OPT_ACTION_EXECUTION,
                "app-inventory" => BugCode.APP_INV_SCAN,
                "security-health" => BugCode.SEC_HEALTH_QUERY,
                _ => BugCode.Unknown
            }
        };
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeClassifierTests"`
Expected: PASS (all 4).

- [ ] **Step 6: Commit**

```bash
git add src/Ralven.Contracts/BugCode.cs src/Ralven.Windows/Diagnostics/BugCodeClassifier.cs tests/Ralven.Tests/Windows/BugCodeClassifierTests.cs
git commit -m "feat(contracts): add APP_INV_SCAN and SEC_HEALTH_QUERY bug codes

Both cover real existing failure paths (Applications inventory scan,
Security Center health read) that today fall back to a generic
unavailable message with no classification at all."
```

---

### Task 3: `BugCodeCatalog` category derivation + localized category labels

**Files:**
- Create: `src/Ralven.Contracts/BugCodeCatalog.cs`
- Modify: `src/Ralven.App/Resources/Strings.resx`, `src/Ralven.App/Resources/Strings.pt-BR.resx`, `src/Ralven.App/Resources/Strings.es.resx`
- Test: `tests/Ralven.Tests/Contracts/BugCodeCatalogTests.cs` (new)

**Interfaces:**
- Produces: `Ralven.Contracts.BugCodeCatalog.GetCategory(BugCode) -> string` (e.g. `"BRK"`), `BugCodeCatalog.GetCategoryResourceKey(BugCode) -> string?` (e.g. `"BugCode.Category.Broker"`, or `null` if the category prefix isn't in the known table).
- Produces resx keys: `BugCode.Category.App`, `BugCode.Category.Updater`, `BugCode.Category.Broker`, `BugCode.Category.Network`, `BugCode.Category.FiveM`, `BugCode.Category.GtaV`, `BugCode.Category.Windows`, `BugCode.Category.Config`, `BugCode.Category.System`, `BugCode.Category.Security`, `BugCode.Category.Unknown`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.Contracts;

public sealed class BugCodeCatalogTests
{
    [Theory]
    [InlineData(BugCode.BRK_ACTION_EXECUTION, "BRK", "BugCode.Category.Broker")]
    [InlineData(BugCode.APP_INV_SCAN, "APP", "BugCode.Category.App")]
    [InlineData(BugCode.SEC_HEALTH_QUERY, "SEC", "BugCode.Category.Security")]
    [InlineData(BugCode.FIVEM_CACHE_OPERATION, "FIVEM", "BugCode.Category.FiveM")]
    public void GetCategory_And_GetCategoryResourceKey_MatchKnownCodes(
        BugCode code, string expectedCategory, string expectedResourceKey)
    {
        Assert.Equal(expectedCategory, BugCodeCatalog.GetCategory(code));
        Assert.Equal(expectedResourceKey, BugCodeCatalog.GetCategoryResourceKey(code));
    }

    [Fact]
    public void GetCategoryResourceKey_EveryDefinedBugCode_ResolvesToAKnownCategory()
    {
        foreach (var code in Enum.GetValues<BugCode>())
        {
            if (code == BugCode.Unknown) continue;

            Assert.NotNull(BugCodeCatalog.GetCategoryResourceKey(code));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeCatalogTests"`
Expected: FAIL (`BugCodeCatalog` doesn't exist).

- [ ] **Step 3: Implement `BugCodeCatalog`**

```csharp
namespace Ralven.Contracts;

/// <summary>
/// Groups every <see cref="BugCode"/> into its category (the prefix before
/// the first underscore, e.g. "BRK" for <see cref="BugCode.BRK_ACTION_EXECUTION"/>).
/// Categories are few and stable, so — unlike individual codes — each one
/// can carry a translated resource key without a per-code translation step;
/// a newly appended <see cref="BugCode"/> is automatically covered as long
/// as it reuses an existing category prefix.
/// </summary>
public static class BugCodeCatalog
{
    /// <summary>Maps each known category prefix to its localization resource key.</summary>
    public static readonly IReadOnlyDictionary<string, string> CategoryResourceKeys =
        new Dictionary<string, string>
        {
            ["APP"] = "BugCode.Category.App",
            ["UPD"] = "BugCode.Category.Updater",
            ["BRK"] = "BugCode.Category.Broker",
            ["NET"] = "BugCode.Category.Network",
            ["FIVEM"] = "BugCode.Category.FiveM",
            ["GTAV"] = "BugCode.Category.GtaV",
            ["WIN"] = "BugCode.Category.Windows",
            ["CFG"] = "BugCode.Category.Config",
            ["SYS"] = "BugCode.Category.System",
            ["SEC"] = "BugCode.Category.Security",
        };

    /// <summary>Extracts the category prefix from a <see cref="BugCode"/> (e.g. "BRK").</summary>
    public static string GetCategory(BugCode code)
    {
        var name = code.ToString();
        var separatorIndex = name.IndexOf('_');
        return separatorIndex > 0 ? name[..separatorIndex] : name;
    }

    /// <summary>Resource key for the category's localized label, or null if the category is unknown.</summary>
    public static string? GetCategoryResourceKey(BugCode code) =>
        CategoryResourceKeys.TryGetValue(GetCategory(code), out var key) ? key : null;
}
```

- [ ] **Step 4: Add the 11 resx keys to all three files**

In `src/Ralven.App/Resources/Strings.resx`, find the line `<data name="Report.RestartNotNeeded" xml:space="preserve"><value>No restart is required.</value></data>` and insert immediately after it:

```xml
  <data name="BugCode.Category.App" xml:space="preserve"><value>Application</value></data>
  <data name="BugCode.Category.Updater" xml:space="preserve"><value>Update</value></data>
  <data name="BugCode.Category.Broker" xml:space="preserve"><value>Administrative privileges</value></data>
  <data name="BugCode.Category.Network" xml:space="preserve"><value>Network</value></data>
  <data name="BugCode.Category.FiveM" xml:space="preserve"><value>FiveM</value></data>
  <data name="BugCode.Category.GtaV" xml:space="preserve"><value>GTA V</value></data>
  <data name="BugCode.Category.Windows" xml:space="preserve"><value>Windows</value></data>
  <data name="BugCode.Category.Config" xml:space="preserve"><value>Configuration</value></data>
  <data name="BugCode.Category.System" xml:space="preserve"><value>System</value></data>
  <data name="BugCode.Category.Security" xml:space="preserve"><value>Security</value></data>
  <data name="BugCode.Category.Unknown" xml:space="preserve"><value>Unclassified</value></data>
```

In `src/Ralven.App/Resources/Strings.pt-BR.resx`, find `<data name="Report.RestartNotNeeded" xml:space="preserve"><value>Não é necessário reiniciar.</value></data>` and insert immediately after it:

```xml
  <data name="BugCode.Category.App" xml:space="preserve"><value>Aplicativo</value></data>
  <data name="BugCode.Category.Updater" xml:space="preserve"><value>Atualização</value></data>
  <data name="BugCode.Category.Broker" xml:space="preserve"><value>Privilégios administrativos</value></data>
  <data name="BugCode.Category.Network" xml:space="preserve"><value>Rede</value></data>
  <data name="BugCode.Category.FiveM" xml:space="preserve"><value>FiveM</value></data>
  <data name="BugCode.Category.GtaV" xml:space="preserve"><value>GTA V</value></data>
  <data name="BugCode.Category.Windows" xml:space="preserve"><value>Windows</value></data>
  <data name="BugCode.Category.Config" xml:space="preserve"><value>Configuração</value></data>
  <data name="BugCode.Category.System" xml:space="preserve"><value>Sistema</value></data>
  <data name="BugCode.Category.Security" xml:space="preserve"><value>Segurança</value></data>
  <data name="BugCode.Category.Unknown" xml:space="preserve"><value>Não classificado</value></data>
```

In `src/Ralven.App/Resources/Strings.es.resx`, find `<data name="Report.RestartNotNeeded" xml:space="preserve"><value>No se requiere reiniciar.</value></data>` and insert immediately after it:

```xml
  <data name="BugCode.Category.App" xml:space="preserve"><value>Aplicación</value></data>
  <data name="BugCode.Category.Updater" xml:space="preserve"><value>Actualización</value></data>
  <data name="BugCode.Category.Broker" xml:space="preserve"><value>Privilegios administrativos</value></data>
  <data name="BugCode.Category.Network" xml:space="preserve"><value>Red</value></data>
  <data name="BugCode.Category.FiveM" xml:space="preserve"><value>FiveM</value></data>
  <data name="BugCode.Category.GtaV" xml:space="preserve"><value>GTA V</value></data>
  <data name="BugCode.Category.Windows" xml:space="preserve"><value>Windows</value></data>
  <data name="BugCode.Category.Config" xml:space="preserve"><value>Configuración</value></data>
  <data name="BugCode.Category.System" xml:space="preserve"><value>Sistema</value></data>
  <data name="BugCode.Category.Security" xml:space="preserve"><value>Seguridad</value></data>
  <data name="BugCode.Category.Unknown" xml:space="preserve"><value>Sin clasificar</value></data>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~BugCodeCatalogTests"`
Expected: PASS (5/5).

- [ ] **Step 6: Confirm resx still builds (no duplicate-key XML error)**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Ralven.Contracts/BugCodeCatalog.cs src/Ralven.App/Resources/Strings.resx src/Ralven.App/Resources/Strings.pt-BR.resx src/Ralven.App/Resources/Strings.es.resx tests/Ralven.Tests/Contracts/BugCodeCatalogTests.cs
git commit -m "feat(contracts): add BugCodeCatalog category lookup with localized labels"
```

---

### Task 4: Per-action `BugCode` on the journal and structured report

**Files:**
- Modify: `src/Ralven.Windows/Engine/TransactionJournal.cs`
- Modify: `src/Ralven.Windows/Engine/WindowsTransactionEngine.cs:1132-1137`
- Modify: `src/Ralven.Contracts/OptimizationReportDto.cs`
- Modify: `src/Ralven.Windows/Engine/OptimizationReportBuilder.cs`
- Test: `tests/Ralven.Tests/Windows/OptimizationReportBuilderTests.cs`

**Interfaces:**
- Consumes: `BugCodeClassifier.ClassifyOptimizationException(Exception, string? actionId)` from Task 1/2 (`Ralven.Windows.Diagnostics`).
- Produces: `WindowsActionJournalEntry.BugCode` (nullable `BugCode`), `OptimizationReportLineDto.BugCode` (nullable `BugCode`), both populated end to end for any action that fails via a real caught exception.

- [ ] **Step 1: Write the failing test**

Add to `tests/Ralven.Tests/Windows/OptimizationReportBuilderTests.cs`:

```csharp
    [Fact]
    public void Build_PropagatesBugCodeFromJournalEntryToReportLine()
    {
        var entry = Entry(1, OptimizationActionIds.DisableBackgroundCapture, ActionExecutionOutcome.Failed);
        entry.BugCode = BugCode.WIN_GAMING_MODE;
        var journal = Journal(entry);

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Balanced);

        Assert.Equal(BugCode.WIN_GAMING_MODE, report.Lines[0].BugCode);
    }

    [Fact]
    public void Build_LineWithoutBugCode_ReportsNullNotAFakeDefault()
    {
        var journal = Journal(Entry(1, OptimizationActionIds.EnableGameMode, ActionExecutionOutcome.Applied));

        var report = OptimizationReportBuilder.Build(journal, OptimizationProfile.Light);

        Assert.Null(report.Lines[0].BugCode);
    }
```

(`Ralven.Contracts` is already `using` at the top of that test file.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~OptimizationReportBuilderTests"`
Expected: FAIL (`BugCode` property doesn't exist on the entry/line yet).

- [ ] **Step 3: Add `BugCode` to the journal entry**

In `src/Ralven.Windows/Engine/TransactionJournal.cs`, right after the existing:

```csharp
    /// <summary>Reason an action was skipped or not run, for the report.</summary>
    public string? OutcomeReason { get; set; }
```

add:

```csharp

    /// <summary>Stable classification of the failure, when one was caught. Null for
    /// actions that succeeded, were skipped, or failed through a path with no
    /// live exception object to classify (e.g. an interrupted-run recovery marker).</summary>
    public BugCode? BugCode { get; set; }
```

- [ ] **Step 4: Set it at the one real exception-catching site in the engine**

In `src/Ralven.Windows/Engine/WindowsTransactionEngine.cs`, add `using Ralven.Windows.Diagnostics;` to the top usings block (alongside the existing `using Ralven.Contracts;` and `using Ralven.Windows.Actions;`). Then change:

```csharp
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            item.Entry.Error = exception.ToString();
            item.Entry.State = ActionJournalState.Failed;
            item.Entry.Outcome = ActionExecutionOutcome.Failed;
            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
```

to:

```csharp
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            item.Entry.Error = exception.ToString();
            item.Entry.State = ActionJournalState.Failed;
            item.Entry.Outcome = ActionExecutionOutcome.Failed;
            item.Entry.BugCode = BugCodeClassifier.ClassifyOptimizationException(
                exception, item.Action.Metadata.Id);
            item.Entry.CompletedAtUtc = DateTimeOffset.UtcNow;
```

(This is the only site in the file where `ActionJournalState.Failed` is set from a live caught exception with a specific action in scope — the interrupted-run recovery markers at lines ~303 and ~684 use a fixed reason string with no exception object and are intentionally left without a `BugCode`.)

- [ ] **Step 5: Add `BugCode` to the report line DTO and propagate it**

In `src/Ralven.Contracts/OptimizationReportDto.cs`, change `OptimizationReportLineDto` from:

```csharp
    public required ActionExecutionOutcome Outcome { get; init; }

    public string? Reason { get; init; }
}
```

to:

```csharp
    public required ActionExecutionOutcome Outcome { get; init; }

    public string? Reason { get; init; }

    /// <summary>Stable classification of the failure, when one was caught.</summary>
    public BugCode? BugCode { get; init; }
}
```

In `src/Ralven.Windows/Engine/OptimizationReportBuilder.cs`, in the `Build` method's line-construction loop, change:

```csharp
            lines.Add(new OptimizationReportLineDto
            {
                Sequence = entry.Sequence,
                ActionId = entry.ActionId,
                ActionName = definition?.Name ?? entry.ActionId,
                Category = definition?.Category ?? ActionCategory.Safety,
                Outcome = outcome,
                Reason = entry.OutcomeReason
            });
```

to:

```csharp
            lines.Add(new OptimizationReportLineDto
            {
                Sequence = entry.Sequence,
                ActionId = entry.ActionId,
                ActionName = definition?.Name ?? entry.ActionId,
                Category = definition?.Category ?? ActionCategory.Safety,
                Outcome = outcome,
                Reason = entry.OutcomeReason,
                BugCode = entry.BugCode
            });
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~OptimizationReportBuilderTests"`
Expected: PASS (all, including the two new ones).

- [ ] **Step 7: Full build**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Ralven.Windows/Engine/TransactionJournal.cs src/Ralven.Windows/Engine/WindowsTransactionEngine.cs src/Ralven.Contracts/OptimizationReportDto.cs src/Ralven.Windows/Engine/OptimizationReportBuilder.cs tests/Ralven.Tests/Windows/OptimizationReportBuilderTests.cs
git commit -m "feat(windows): classify a BugCode per failed optimization action

Previously only the whole run got an (approximated, synthetic-exception)
BugCode for telemetry. Now the real exception at the point of failure
is classified and carried on the journal entry and report line, so a
specific action's root cause is identifiable without guessing."
```

---

### Task 5: Show the code in the Optimizer Result screen; drop the synthetic-exception hack

**Files:**
- Create: `src/Ralven.App/ViewModels/OptimizationFailureMessageFormatter.cs`
- Modify: `src/Ralven.App/ViewModels/MainViewModel.Report.cs`
- Modify: `src/Ralven.App/ViewModels/MainViewModel.Optimization.cs`
- Modify: `src/Ralven.App/Resources/Strings.resx`, `Strings.pt-BR.resx`, `Strings.es.resx`
- Test: `tests/Ralven.Tests/App/OptimizationFailureMessageFormatterTests.cs` (new)

Note: `DisplayModels.cs`'s `ReportLineDisplayItem` shape is unchanged — the composed text still flows through its existing `Reason` property, so this task does not touch that file.

**Interfaces:**
- Consumes: `OptimizationReportLineDto.BugCode`, `OptimizationReportDto.Lines` (Task 4).
- Produces: `OptimizationFailureMessageFormatter.AppendCode(string? message, BugCode? code, Func<string,string> formatCodeSuffix) -> string?` — a small pure static helper (testable without a full `ILocalizationService`/WPF context), reused by every call site in Tasks 5 and 6.

- [ ] **Step 1: Write the failing test for the pure formatter**

```csharp
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

public sealed class OptimizationFailureMessageFormatterTests
{
    [Fact]
    public void AppendCode_WithCode_AppendsFormattedSuffixAfterAnEmDash()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            "Access denied",
            BugCode.WIN_PRIVILEGE,
            code => $"Código do erro: {code}");

        Assert.Equal("Access denied — Código do erro: WIN_PRIVILEGE", result);
    }

    [Fact]
    public void AppendCode_NullCode_ReturnsMessageUnchanged()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            "Access denied",
            null,
            code => $"Código do erro: {code}");

        Assert.Equal("Access denied", result);
    }

    [Fact]
    public void AppendCode_NullMessage_ReturnsJustTheFormattedSuffix()
    {
        var result = OptimizationFailureMessageFormatter.AppendCode(
            null,
            BugCode.WIN_PRIVILEGE,
            code => $"Código do erro: {code}");

        Assert.Equal("Código do erro: WIN_PRIVILEGE", result);
    }
}
```

Note: the `formatCodeSuffix` delegate above is a stand-in for `code => localization.Format("Report.ErrorCodeSuffix", code)` — see Step 3 for the real call sites, which pass a closure over `ILocalizationService`. The test exercises only the pure concatenation rule.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~OptimizationFailureMessageFormatterTests"`
Expected: FAIL (`OptimizationFailureMessageFormatter` doesn't exist).

- [ ] **Step 3: Implement the formatter**

Create `src/Ralven.App/ViewModels/OptimizationFailureMessageFormatter.cs`:

```csharp
using Ralven.Contracts;

namespace Ralven.App.ViewModels;

/// <summary>
/// Appends "— {localized error code text}" to an existing message when a
/// BugCode was captured. Kept as a pure static function so the composition
/// rule is testable without a WPF/localization host.
/// </summary>
public static class OptimizationFailureMessageFormatter
{
    /// <param name="message">The existing localized message/reason, or null/empty.</param>
    /// <param name="code">The classified failure code, or null when none was captured.</param>
    /// <param name="formatCodeSuffix">
    /// Given the raw code as a string (e.g. "WIN_PRIVILEGE"), returns the
    /// localized "Código do erro: WIN_PRIVILEGE"-style suffix, with no
    /// leading separator — this method supplies the em dash.
    /// </param>
    public static string? AppendCode(string? message, BugCode? code, Func<string, string> formatCodeSuffix)
    {
        if (code is null)
        {
            return message;
        }

        var suffix = formatCodeSuffix(code.Value.ToString());
        return string.IsNullOrEmpty(message) ? suffix : $"{message} — {suffix}";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~OptimizationFailureMessageFormatterTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Add one resx key (3 languages)**

In `src/Ralven.App/Resources/Strings.resx`, after the `BugCode.Category.Unknown` line added in Task 3, add:

```xml
  <data name="Report.ErrorCodeSuffix" xml:space="preserve"><value>Error code: {0}</value></data>
```

In `src/Ralven.App/Resources/Strings.pt-BR.resx`, after its `BugCode.Category.Unknown` line, add:

```xml
  <data name="Report.ErrorCodeSuffix" xml:space="preserve"><value>Código do erro: {0}</value></data>
```

In `src/Ralven.App/Resources/Strings.es.resx`, after its `BugCode.Category.Unknown` line, add:

```xml
  <data name="Report.ErrorCodeSuffix" xml:space="preserve"><value>Código de error: {0}</value></data>
```

- [ ] **Step 6: Wire the per-line display in `MainViewModel.Report.cs`**

In `ApplyReport`, change:

```csharp
        foreach (var line in report.Lines)
        {
            var (label, glyph, brushKey) = DescribeOutcome(line.Outcome);
            ReportLines.Add(new ReportLineDisplayItem(
                GetLocalizedActionName(line.ActionId, line.ActionName),
                label,
                glyph,
                brushKey,
                line.Reason));
        }
```

to:

```csharp
        foreach (var line in report.Lines)
        {
            var (label, glyph, brushKey) = DescribeOutcome(line.Outcome);
            var reasonWithCode = OptimizationFailureMessageFormatter.AppendCode(
                line.Reason,
                line.BugCode,
                code => localization.Format("Report.ErrorCodeSuffix", code));
            ReportLines.Add(new ReportLineDisplayItem(
                GetLocalizedActionName(line.ActionId, line.ActionName),
                label,
                glyph,
                brushKey,
                reasonWithCode));
        }
```

`ReportLineDisplayItem`'s shape (`ActionName, OutcomeLabel, OutcomeGlyph, OutcomeBrushKey, Reason`) is unchanged — no XAML binding changes needed since `Reason` already binds to the same place.

- [ ] **Step 7: Wire the overall summary in `MainViewModel.Optimization.cs`**

Replace the synthetic-exception hack:

```csharp
            if (!result.Succeeded && result.Report is not null)
            {
                // Use the first failed action's ID for bug classification
                var failedActionId = result.Report.Lines
                    .Where(l => l.Outcome is ActionExecutionOutcome.Failed or ActionExecutionOutcome.RollbackFailed)
                    .Select(l => l.ActionId)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(failedActionId))
                {
                    telemetryBugCode = BugCodeClassifier.ClassifyOptimizationException(new InvalidOperationException(), failedActionId);
                }
            }
```

with a direct read of the already-classified per-action code (no more fake exception):

```csharp
            if (!result.Succeeded && result.Report is not null)
            {
                telemetryBugCode = result.Report.Lines
                    .Where(l => l.Outcome is ActionExecutionOutcome.Failed or ActionExecutionOutcome.RollbackFailed)
                    .Select(l => l.BugCode)
                    .FirstOrDefault(code => code is not null);
            }
```

Then, in `HandleOptimizationResultAsync`, append the code to the visible headline:

```csharp
    private async Task HandleOptimizationResultAsync(AppOptimizationResult result)
    {
        ProgressPercent = result.Succeeded ? 100 : ProgressPercent;
        FinalizeHeadline(result.Succeeded
            ? localization.GetString("Status.OptimizationCompleted")
            : OptimizationFailureMessageFormatter.AppendCode(
                result.Summary,
                result.Report?.Lines
                    .Where(l => l.Outcome is ActionExecutionOutcome.Failed or ActionExecutionOutcome.RollbackFailed)
                    .Select(l => l.BugCode)
                    .FirstOrDefault(code => code is not null),
                code => localization.Format("Report.ErrorCodeSuffix", code)));
        ApplyReport(result.Report);
        lastTransactionId = result.TransactionId;
        ApplyComparison(result.Comparison);
        ApplyHistory(await service.LoadHistoryAsync());
    }
```

- [ ] **Step 8: Full build**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 9: Run the .NET test suite**

Run: `dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1`
Expected: all tests pass, including every test added in Tasks 1-5.

- [ ] **Step 10: Commit**

```bash
git add src/Ralven.App/ViewModels/OptimizationFailureMessageFormatter.cs src/Ralven.App/ViewModels/MainViewModel.Report.cs src/Ralven.App/ViewModels/MainViewModel.Optimization.cs src/Ralven.App/Resources/Strings.resx src/Ralven.App/Resources/Strings.pt-BR.resx src/Ralven.App/Resources/Strings.es.resx tests/Ralven.Tests/App/OptimizationFailureMessageFormatterTests.cs
git commit -m "feat(app): show the error code on the Optimizer Result screen

Both the overall failure headline and each failed action's line now
end with 'Código do erro: XXX', using the BugCode classified at the
point of failure instead of a synthetic exception built after the
fact just for telemetry."
```

---

### Task 6: Wire the two new codes into Applications and System pages

**Files:**
- Modify: `src/Ralven.App/ViewModels/ApplicationsPageViewModel.cs`
- Modify: `src/Ralven.Windows/Infrastructure/WindowsSystemHealthInspector.cs`
- Modify: `src/Ralven.App/ViewModels/MainViewModel.System.cs`
- Test: `tests/Ralven.Tests/Windows/WindowsSystemHealthInspectorTests.cs` (extend if it exists, else create alongside existing inspector tests — check `tests/Ralven.Tests/Windows/` for the current file name before writing)

No resx changes in this task — it reuses the `Report.ErrorCodeSuffix` key added in Task 5.

**Interfaces:**
- Consumes: `OptimizationFailureMessageFormatter.AppendCode` (Task 5), `BugCodeClassifier.ClassifyException` (Tasks 1-2).
- Produces: `WindowsSecurityProviderHealth.BugCode` (nullable `BugCode`, new record parameter with default `null` so existing call sites compile unchanged).

- [ ] **Step 1: Check for an existing inspector test file**

Run: `find tests/Ralven.Tests -iname "*SystemHealth*" -o -iname "*ApplicationInventory*"`

If a file is found, add the new tests below to it instead of creating a new one; adjust the `namespace`/class wrapper to match what's there.

- [ ] **Step 2: Write the failing test for `WindowsSecurityProviderHealth.BugCode`**

```csharp
using Ralven.Contracts;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class WindowsSystemHealthInspectorTests
{
    [Fact]
    public void Inspector_WhenNativeCallThrows_ProviderHealthCarriesClassifiedBugCode()
    {
        var inspector = new WindowsSystemHealthInspector((
            WindowsSystemHealthInspector.SecurityProvider provider,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health) =>
        {
            health = default;
            throw new DllNotFoundException("wscapi.dll missing");
        });

        var snapshot = inspector.InspectAsync().GetAwaiter().GetResult();

        Assert.Equal(BugCode.SEC_HEALTH_QUERY, snapshot.Antivirus.BugCode);
        Assert.Equal(WindowsSecurityHealthState.Unavailable, snapshot.Antivirus.State);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~WindowsSystemHealthInspectorTests"`
Expected: FAIL (`BugCode` property doesn't exist on `WindowsSecurityProviderHealth`; the constructor call with 3 args doesn't compile yet — that's expected).

- [ ] **Step 4: Add `BugCode` to `WindowsSecurityProviderHealth` and classify in the catch block**

In `src/Ralven.Windows/Infrastructure/WindowsSystemHealthInspector.cs`, add `using Ralven.Windows.Diagnostics;` at the top, then change:

```csharp
public sealed record WindowsSecurityProviderHealth(
    WindowsSecurityHealthState State,
    int HResult)
{
    public bool IsAvailable => State != WindowsSecurityHealthState.Unavailable;
}
```

to:

```csharp
public sealed record WindowsSecurityProviderHealth(
    WindowsSecurityHealthState State,
    int HResult,
    BugCode? BugCode = null)
{
    public bool IsAvailable => State != WindowsSecurityHealthState.Unavailable;
}
```

and change the `catch` block:

```csharp
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return new WindowsSecurityProviderHealth(
                WindowsSecurityHealthState.Unavailable,
                exception.HResult);
        }
```

to:

```csharp
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException)
        {
            return new WindowsSecurityProviderHealth(
                WindowsSecurityHealthState.Unavailable,
                exception.HResult,
                BugCodeClassifier.ClassifyException(exception, "security-health"));
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Ralven.Tests/Ralven.Tests.csproj --filter "FullyQualifiedName~WindowsSystemHealthInspectorTests"`
Expected: PASS.

- [ ] **Step 6: Surface the code in the System page status text**

`MainViewModel.System.cs` has two distinct failure paths that both end up setting `windowsSystemHealthStatusKey = "System.Health.Status.Unavailable"`: the per-provider case (some/all of Antivirus/Firewall/AutomaticUpdates report `Unavailable`, each already carrying a `BugCode` from Step 4) and the outer case where `windowsSystemHealthInspector.InspectAsync()` itself throws and `windowsSystemHealth` becomes `null` (no snapshot to read a `BugCode` from at all). Add a field for the second case.

Add `using Ralven.Windows.Diagnostics;` to the file's usings (currently `Ralven.App.Services`, `Ralven.Contracts`, `Ralven.Windows.Infrastructure`).

Add a field next to `windowsSystemHealthStatusKey`:

```csharp
    private string windowsSystemHealthStatusKey = "System.Health.Status.Loading";
    private BugCode? windowsSystemHealthBugCode;
```

Change `RefreshWindowsSystemHealthAsync`:

```csharp
        try
        {
            windowsSystemHealth = await windowsSystemHealthInspector.InspectAsync();
            windowsSystemHealthStatusKey = !windowsSystemHealth.Antivirus.IsAvailable
                && !windowsSystemHealth.Firewall.IsAvailable
                && !windowsSystemHealth.AutomaticUpdates.IsAvailable
                    ? "System.Health.Status.Unavailable"
                    : windowsSystemHealth.IsPartial
                        ? "System.Health.Status.Partial"
                        : "System.Health.Status.Ready";
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsSystemHealth = null;
            windowsSystemHealthStatusKey = "System.Health.Status.Unavailable";
        }
```

to:

```csharp
        try
        {
            windowsSystemHealth = await windowsSystemHealthInspector.InspectAsync();
            windowsSystemHealthBugCode = null;
            windowsSystemHealthStatusKey = !windowsSystemHealth.Antivirus.IsAvailable
                && !windowsSystemHealth.Firewall.IsAvailable
                && !windowsSystemHealth.AutomaticUpdates.IsAvailable
                    ? "System.Health.Status.Unavailable"
                    : windowsSystemHealth.IsPartial
                        ? "System.Health.Status.Partial"
                        : "System.Health.Status.Ready";
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            windowsSystemHealth = null;
            windowsSystemHealthBugCode = BugCodeClassifier.ClassifyException(exception, "security-health");
            windowsSystemHealthStatusKey = "System.Health.Status.Unavailable";
        }
```

Change the display property:

```csharp
    public string WindowsSystemHealthStatusMessage => localization.GetString(
        windowsSystemHealthStatusKey);
```

to:

```csharp
    public string WindowsSystemHealthStatusMessage => OptimizationFailureMessageFormatter.AppendCode(
        localization.GetString(windowsSystemHealthStatusKey),
        windowsSystemHealthStatusKey != "System.Health.Status.Unavailable"
            ? null
            : windowsSystemHealthBugCode
                ?? windowsSystemHealth?.Antivirus.BugCode
                ?? windowsSystemHealth?.Firewall.BugCode
                ?? windowsSystemHealth?.AutomaticUpdates.BugCode,
        code => localization.Format("Report.ErrorCodeSuffix", code));
```

(`OptimizationFailureMessageFormatter` is in `Ralven.App.ViewModels`, the same namespace this file's `partial class MainViewModel` is declared in — no new `using` needed for it.)

- [ ] **Step 7: Surface the code in the Applications page inventory-unavailable message**

In `src/Ralven.App/ViewModels/ApplicationsPageViewModel.cs`, add a private field `private BugCode? inventoryBugCode;` near the existing `private bool inventoryUnavailable;`. Change the catch block:

```csharp
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            inventoryUnavailable = true;
            InventoryStatusMessage = localization.GetString(
                "Applications.Inventory.Status.Unavailable");
```

to:

```csharp
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            inventoryUnavailable = true;
            inventoryBugCode = BugCodeClassifier.ClassifyException(exception, "app-inventory");
            InventoryStatusMessage = OptimizationFailureMessageFormatter.AppendCode(
                localization.GetString("Applications.Inventory.Status.Unavailable"),
                inventoryBugCode,
                code => localization.Format("Report.ErrorCodeSuffix", code));
```

Add `using Ralven.Windows.Diagnostics;` to this file's usings if not already present (check the top of the file first).

Also reset it on success: right after `inventoryUnavailable = false;` in the `try` block, add `inventoryBugCode = null;`.

- [ ] **Step 8: Full build**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 9: Run the full .NET test suite**

Run: `dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1`
Expected: all pass.

- [ ] **Step 10: Commit**

```bash
git add src/Ralven.App/ViewModels/ApplicationsPageViewModel.cs src/Ralven.Windows/Infrastructure/WindowsSystemHealthInspector.cs src/Ralven.App/ViewModels/MainViewModel.System.cs tests/Ralven.Tests/Windows/WindowsSystemHealthInspectorTests.cs
git commit -m "feat(app): show error code when app inventory or security health is unavailable"
```

---

### Task 7: Bug report clipboard shows category + code, not just the raw enum

**Files:**
- Modify: `src/Ralven.App/Views/BugReportWindow.xaml.cs`
- Modify: `src/Ralven.App/Resources/Strings.resx`, `Strings.pt-BR.resx`, `Strings.es.resx`
- Test: `tests/Ralven.Tests/App/BugReportServiceTests.cs` (extend — check its current content first for where clipboard formatting is tested, since `FormatForClipboard` is a private method on the code-behind; if there's no existing test seam for it, add a small internal static helper instead, matching the pattern from Task 5)

**Interfaces:**
- Consumes: `BugCodeCatalog.GetCategoryResourceKey(BugCode)` (Task 3).

- [ ] **Step 1: Check whether clipboard formatting is already covered by a test**

Run: `grep -n "FormatForClipboard\|Clipboard" tests/Ralven.Tests/App/BugReportServiceTests.cs`

- [ ] **Step 2: Update the resx key to take a second argument**

In `src/Ralven.App/Resources/Strings.resx`, change:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Bug code: {0}</value></data>
```

to:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Bug code: {0} ({1})</value></data>
```

In `src/Ralven.App/Resources/Strings.pt-BR.resx`, change:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Código do bug: {0}</value></data>
```

to:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Código do bug: {0} ({1})</value></data>
```

In `src/Ralven.App/Resources/Strings.es.resx`, change:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Código del error: {0}</value></data>
```

to:

```xml
  <data name="BugReport.Clipboard.BugCode" xml:space="preserve"><value>Código del error: {0} ({1})</value></data>
```

- [ ] **Step 3: Pass the category label at the call site**

In `src/Ralven.App/Views/BugReportWindow.xaml.cs`, add `using Ralven.Contracts;` if not already present (check top of file — it likely already has it via `BugCode` usage), then change:

```csharp
        builder.AppendLine(F("BugReport.Clipboard.BugCode", submission.BugCode.ToString()));
```

to:

```csharp
        var categoryResourceKey = BugCodeCatalog.GetCategoryResourceKey(submission.BugCode)
            ?? "BugCode.Category.Unknown";
        builder.AppendLine(F(
            "BugReport.Clipboard.BugCode",
            submission.BugCode.ToString(),
            T(categoryResourceKey)));
```

- [ ] **Step 4: If Step 1 found an existing clipboard test, update its expected string**

The expected clipboard text in that test now has `Bug code: {CODE} ({Category})` instead of `Bug code: {CODE}` — update the assertion to match (e.g. append ` (Application)` or the pt-BR equivalent depending on which resx the test runs under).

If Step 1 found no such test, skip straight to Step 5 — this file is WPF code-behind with no existing seam for a fully isolated unit test of `FormatForClipboard`, and adding a UI test harness for it is out of scope for this plan.

- [ ] **Step 5: Full build**

Run: `dotnet build Ralven.slnx --configuration Release`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 6: Run the full .NET test suite**

Run: `dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Ralven.App/Views/BugReportWindow.xaml.cs src/Ralven.App/Resources/Strings.resx src/Ralven.App/Resources/Strings.pt-BR.resx src/Ralven.App/Resources/Strings.es.resx tests/Ralven.Tests/App/BugReportServiceTests.cs
git commit -m "feat(app): show bug code category in the copyable bug report text"
```

---

### Task 8: Worker allowlist + dashboard category label

**Files:**
- Modify: `infra/cloudflare-worker/src/bugCodes.js`
- Test: `infra/cloudflare-worker/test/bugCodes.test.js` (new)
- Modify: `infra/dashboard/assets/charts.js`
- Modify: `infra/dashboard/test/charts.test.js`

**Interfaces:**
- Produces (Worker): `ALLOWED_BUG_CODES` includes `'APP_INV_SCAN'` and `'SEC_HEALTH_QUERY'`.
- Produces (Dashboard): `toBugReportRow` renders the bug-code cell as `"{code} — {category label}"` when the code's category is known, else the raw code unchanged.

- [ ] **Step 1: Write the failing Worker test**

Create `infra/cloudflare-worker/test/bugCodes.test.js` (match the existing test runner style used by `infra/cloudflare-worker/test/migrations.test.js` — check its import line for the test framework in use, e.g. `node:test` or `vitest`, and mirror it):

```javascript
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ALLOWED_BUG_CODES } from '../src/bugCodes.js';

test('ALLOWED_BUG_CODES includes the newly added app inventory and security health codes', () => {
  assert.ok(ALLOWED_BUG_CODES.has('APP_INV_SCAN'));
  assert.ok(ALLOWED_BUG_CODES.has('SEC_HEALTH_QUERY'));
});
```

(If step 1's inspection shows the repo uses `vitest` instead of `node:test`, replace the first two imports with `import { test, expect } from 'vitest';` and the assertions with `expect(ALLOWED_BUG_CODES.has('APP_INV_SCAN')).toBe(true);` etc. — check one existing test file before writing this one.)

- [ ] **Step 2: Run test to verify it fails**

Run (from `infra/cloudflare-worker`): `npm test -- bugCodes`
Expected: FAIL (codes not yet in the set).

- [ ] **Step 3: Add the two codes to the allowlist**

In `infra/cloudflare-worker/src/bugCodes.js`, add `'APP_INV_SCAN', 'SEC_HEALTH_QUERY',` to the `ALLOWED_BUG_CODES` set (anywhere in the list; append at the end before the closing `]);` for minimal diff):

```javascript
  'CFG_VALIDATION', 'CFG_MIGRATION', 'CFG_ENV_VAR', 'SYS_FILESYSTEM', 'SYS_PROCESS', 'SYS_MEMORY',
  'SYS_JSON', 'SYS_CRYPTO', 'SYS_TIME', 'SYS_PATH', 'SYS_ASSEMBLY',
  'APP_INV_SCAN', 'SEC_HEALTH_QUERY',
]);
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `infra/cloudflare-worker`): `npm test -- bugCodes`
Expected: PASS.

- [ ] **Step 5: Run the full Worker suite**

Run (from `infra/cloudflare-worker`): `npm test`
Expected: all pass (no regression from the allowlist change).

- [ ] **Step 6: Write the failing dashboard test**

In `infra/dashboard/test/charts.test.js`, update the existing test `'toBugReportRow maps a row into the bug report table\'s column order'` (found at the row with `bug_code: 'APP_OPT_ACTION_EXECUTION'`) — change its expected cell from the raw code to the code-plus-category form:

```javascript
  assert.deepEqual(cells.slice(1), [
    'Falha na otimização',
    'APP_OPT_ACTION_EXECUTION — Aplicativo',
    'O preset não terminou',
    '1.0.4',
    'Médio',
    'Production',
    'user@example.com',
    'sim',
  ]);
```

Leave the second test (`bug_code: null`) unchanged — a missing code must still render whatever `fallback(null)` already renders (the `—` placeholder), untouched by this change.

- [ ] **Step 7: Run test to verify it fails**

Run (from `infra/dashboard`): `npm test -- charts`
Expected: FAIL (current `toBugReportRow` still emits the raw code only).

- [ ] **Step 8: Implement the category label lookup and use it in `toBugReportRow`**

In `infra/dashboard/assets/charts.js`, add near the top of the file (after existing imports/constants):

```javascript
// Mirrors Ralven.Contracts.BugCodeCatalog's category prefixes (the part of
// the code before the first underscore). Kept as a small hand-maintained
// map, same convention already used for bug_code itself in this dashboard —
// there is no shared package between the Worker/dashboard and the .NET app.
const BUG_CODE_CATEGORY_LABELS = {
  APP: 'Aplicativo',
  UPD: 'Atualização',
  BRK: 'Privilégios administrativos',
  NET: 'Rede',
  FIVEM: 'FiveM',
  GTAV: 'GTA V',
  WIN: 'Windows',
  CFG: 'Configuração',
  SYS: 'Sistema',
  SEC: 'Segurança',
};

function bugCodeWithCategory(code) {
  if (!code) return fallback(code);
  const category = BUG_CODE_CATEGORY_LABELS[code.split('_')[0]];
  return category ? `${code} — ${category}` : code;
}
```

Then change `toBugReportRow`:

```javascript
export function toBugReportRow(row) {
  return [
    formatTimestamp(row.received_at),
    fallback(row.category),
    bugCodeWithCategory(row.bug_code),
    truncate(row.summary, 60),
    formatAppVersion(row.app_version),
    fallback(row.profile),
    fallback(row.environment),
    fallback(row.email),
    row.log_text ? 'sim' : 'não',
  ];
}
```

- [ ] **Step 9: Run test to verify it passes**

Run (from `infra/dashboard`): `npm test -- charts`
Expected: PASS.

- [ ] **Step 10: Run the full dashboard suite**

Run (from `infra/dashboard`): `npm test`
Expected: all pass.

- [ ] **Step 11: Commit**

```bash
git add infra/cloudflare-worker/src/bugCodes.js infra/cloudflare-worker/test/bugCodes.test.js infra/dashboard/assets/charts.js infra/dashboard/test/charts.test.js
git commit -m "feat(worker,dashboard): allow the new bug codes and show category next to the raw code

Dashboard bug report table showed a raw enum string with no context.
It now appends a short category label (mirroring Ralven.Contracts.BugCodeCatalog)
next to the code, e.g. 'APP_OPT_ACTION_EXECUTION — Aplicativo'."
```

---

## Final Verification

- [ ] Run the full .NET suite: `dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1`
- [ ] `dotnet format Ralven.slnx --verify-no-changes`
- [ ] `.\scripts\Verify-Safety.ps1`
- [ ] `git diff --check`
- [ ] From `infra/cloudflare-worker`: `npm test` and `npm audit`
- [ ] From `infra/dashboard`: run its test/lint/typecheck/build scripts from `package.json`
- [ ] Rebuild the dev shortcut per `AI_RULES.md`: `.\scripts\Install-DevelopmentShortcut.ps1 -Build`
- [ ] Manually run one optimization with a deliberately-failing scope (or the existing demo/simulation mode) and confirm the Result screen shows "… Código do erro: XXX" for the failed action and the overall summary.

## Explicitly out of scope (see spec's "Fora de escopo" + trims made while grounding this plan)

- No `BILL_*` codes: the app has no checkout-initiating code yet (Worker/D1-only foundation per `PROJECT_STATE.md`), so there is no real call site to classify.
- Auth/settings/broker classification expansion beyond what already existed: only the two audited, concretely-verified gaps (Applications inventory, Security Center health) got new codes; broadening further is a follow-up, not blocked by anything in this plan.
- Deploying migration `0008_bug_report_code.sql` to production — separate ops action.
- Touching `infra/dashboard/assets/rendering.js` — reserved for the unintegrated `feat/dashboard-insights` branch to avoid conflicts.
