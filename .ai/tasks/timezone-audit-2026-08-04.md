# Timezone Handling Audit — FiveMCleaner
**Date:** 2026-08-04  
**Auditor:** AI Agent  
**Scope:** Full codebase (C# .NET, Cloudflare Worker JS, Dashboard JS)

---

## Executive Summary

The codebase **largely follows best practices** for timezone handling:
- ✅ Consistent use of `DateTimeOffset` with UTC semantics
- ✅ Clear naming convention (`*Utc` suffix on properties)
- ✅ No `DateTime.Now` in production code
- ✅ Proper UTC storage with local-time conversion only at UI display layer

**Issues found:** 6 (1 High, 2 Medium, 3 Low)

---

## Detailed Findings

### 🔴 HIGH: Inconsistent `DateTimeOffset.Now` vs `DateTimeOffset.UtcNow` in `AppOptimizationService`

**Files:** `src/FiveMCleaner.App/Services/AppOptimizationService.cs` (multiple lines)

**Problem:**
The service uses `DateTimeOffset.Now` for progress timestamps in ~15 locations (lines 289, 304, 318, 327, 559, 576, 623, 708, 737, 849, 875, 906, etc.), but `DateTimeOffset.UtcNow` for internal timestamps like `StartedAtUtc` (line 590) and `CapturedAtUtc` (line 754).

**Impact:**
- Progress timestamps carry local offset; internal timestamps are pure UTC
- If system timezone changes mid-execution (rare but possible via NTP/DST), progress timestamps become inconsistent
- Logs/telemetry mixing both formats requires careful parsing

**Recommendation:**
```csharp
// Change all progress timestamps to:
Timestamp = DateTimeOffset.UtcNow,
```
Or if local time is intentionally needed for UI correlation, add a comment explaining the intent and use a consistent wrapper.

---

### 🟡 MEDIUM: Dashboard displays UTC timestamps without local timezone conversion

**Files:** `infra/dashboard/assets/charts.js` (line 110), `infra/dashboard/assets/app.js`

**Problem:**
The dashboard renders timestamps using `isoString.slice(0, 16).replace('T', ' ')` (line 110 of charts.js), which displays the raw UTC time from the Worker (e.g., "2026-08-04 14:30") without converting to the viewer's local timezone.

**Impact:**
- Users in UTC-3 (Brazil) see times 3 hours ahead
- Users in UTC+9 (Japan) see times 9 hours behind
- Confusion when correlating with local logs

**Recommendation:**
```javascript
// In formatTimestamp function (charts.js:100-111):
export function formatTimestamp(isoString) {
  if (!isoString) return '—';
  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) return '—';
  
  // Convert to local timezone for display
  return date.toLocaleString(undefined, {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false
  }).replace(',', '');
}
```
Or provide a user preference for UTC vs local display.

---

### 🟡 MEDIUM: Redundant `.ToUniversalTime()` call in `StreamingSoftwareDetector`

**File:** `src/FiveMCleaner.App/Services/StreamingSoftwareDetector.cs` (line 96)

**Code:**
```csharp
observedAtUtc.ToUniversalTime()
```

**Problem:**
The parameter is named `observedAtUtc` and is already a UTC `DateTimeOffset` (passed as `DateTimeOffset.UtcNow` from callers). Calling `.ToUniversalTime()` on an already-UTC offset is a no-op but adds confusion.

**Recommendation:**
Remove the redundant call:
```csharp
observedAtUtc  // already UTC
```

---

### 🟢 LOW: `LiveSystemMetricsProvider` uses `DateTimeOffset.Now` for capture timestamp

**File:** `src/FiveMCleaner.App/Services/LiveSystemMetricsProvider.cs` (line 41)

**Code:**
```csharp
return CreateSnapshot(usage, systemInspector.GetSnapshot(), DateTimeOffset.Now);
```

**Problem:**
Uses local time with offset instead of UTC. The snapshot is later displayed via `ToLocalTime()` in MainViewModel (line 802), which double-converts if the system timezone differs from the capture timezone.

**Impact:** Minor — metrics are short-lived and displayed in near-real-time.

**Recommendation:**
Change to `DateTimeOffset.UtcNow` for consistency with the rest of the codebase.

---

### 🟢 LOW: `DateTimeOffset.Now` in `AppOptimizationService.SimulatePlanAsync`

**File:** `src/FiveMCleaner.App/Services/AppOptimizationService.cs` (lines 289, 304, 318, 327)

**Problem:**
Demo/simulation mode uses `DateTimeOffset.Now` for progress timestamps. While this is demo code, it creates inconsistency.

**Recommendation:**
Use `DateTimeOffset.UtcNow` for consistency, or document why local time is preferred for simulation.

---

### 🟢 LOW: Dashboard date filter UX — no timezone indicator

**Files:** `infra/dashboard/assets/app.js` (filter form), `infra/cloudflare-worker/src/filters.js`

**Problem:**
The dashboard's date filter (`from`/`to` inputs) sends calendar dates without timezone. The Worker correctly interprets them as UTC boundaries (using `date(?, '+1 day')` for inclusive end-day), but the UI doesn't inform users that filters operate in UTC.

**Impact:** Users in non-UTC timezones may filter the wrong day's data near midnight boundaries.

**Recommendation:**
Add a small note: "Filters use UTC timezone" near the date inputs, or convert user's local date to UTC before sending.

---

## Correct Patterns (For Reference)

These patterns are **correctly implemented** and should be preserved:

| Pattern | Location | Why Correct |
|---------|----------|-------------|
| `DateTimeOffset.UtcNow` for stored timestamps | Throughout (AppModels, Contracts, Services) | Unambiguous, sortable, portable |
| `*Utc` suffix on properties | `CapturedAtUtc`, `CreatedAtUtc`, `StartedAtUtc`, `TimestampUtc` | Self-documenting |
| `DateTimeOffset.Now` only for UI progress display | `AppOptimizationService` progress reports | Local time correlates with user's clock |
| `.ToLocalTime()` only at display layer | `MainViewModel.cs:802`, `ElevatedBrokerClient.cs:378` | Single conversion point |
| `Process.StartTime.ToUniversalTime().ToFileTimeUtc()` | `AtomicUpdateInstaller.cs:49`, `SilentUpdateInstaller.cs:92`, `Launcher.cs:136`, `Updater.cs:42` | Correct process time handling |
| `entry.TimeCreated.ToUniversalTime()` | `HardwareStabilityInspector.cs:63` | Event log times are local |
| `new Date().toISOString()` | Worker `index.js:154, 178` | ISO 8601 UTC standard |
| `date(?, '+1 day')` for inclusive end-date | `filters.js:34` | Handles UTC date boundaries correctly |

---

## Migration Checklist (If Fixing)

- [x] Change `AppOptimizationService` progress timestamps to `DateTimeOffset.UtcNow`
- [x] Update `LiveSystemMetricsProvider` to use `DateTimeOffset.UtcNow`
- [x] Fix `StreamingSoftwareDetector` redundant `.ToUniversalTime()`
- [x] Update dashboard `formatTimestamp` to convert to local time (or add UTC/local toggle)
- [x] Add "Filters use UTC" hint to dashboard date inputs
- [x] Run full test suite: `dotnet test` + Worker tests + Dashboard tests
- [x] Verify no regression in progress display timing (simulation mode)

---

## Validation Results (Post-Audit)

All validation passes with **zero failures**:

| Check | Status |
|-------|--------|
| `dotnet build --configuration Release` | ✅ 0 warnings, 0 errors |
| `dotnet test --configuration Release` | ✅ 636 passed, 0 failed |
| `scripts/Verify-Safety.ps1` | ✅ Passed |
| `git diff --check` | ✅ Clean |
| Worker `npm test` | ✅ 120 passed |
| Dashboard `npm test` | ✅ 43 passed |
| Website `npm test` | ✅ 3 passed |
| Website `npm run lint` | ✅ Passed |
| Website `npx tsc --noEmit` | ✅ Passed |

No regressions introduced by this audit (read-only analysis).

---

## Fix Implementation (Complete)

All 6 issues have been **fixed** in branch `ai/hermes/timezone-fix` (commit `c92ccc4`):

| Issue | Fix Applied | File |
|-------|-------------|------|
| 🔴 HIGH: Inconsistent `DateTimeOffset.Now` in AppOptimizationService | All 15 progress timestamps changed to `DateTimeOffset.UtcNow` | `AppOptimizationService.cs` |
| 🟡 MEDIUM: Dashboard shows UTC without local conversion | `formatTimestamp()` now uses `toLocaleString()` for local timezone display | `charts.js` |
| 🟡 MEDIUM: Redundant `.ToUniversalTime()` in StreamingSoftwareDetector | Removed redundant call | `StreamingSoftwareDetector.cs` |
| 🟢 LOW: LiveSystemMetricsProvider uses `DateTimeOffset.Now` | Changed to `DateTimeOffset.UtcNow` | `LiveSystemMetricsProvider.cs` |
| 🟢 LOW: Dashboard date filters lack UTC indicator | Added "(UTC)" hint to date inputs | `index.html`, `styles.css` |
| 🟢 LOW: SimulatePlanAsync uses `DateTimeOffset.Now` | Changed to `DateTimeOffset.UtcNow` | `AppOptimizationService.cs` |

**All validations pass after fixes:**
- `dotnet build` — 0 warnings, 0 errors
- `dotnet test` — 636 passed
- Worker `npm test` — 120 passed
- Dashboard `npm test` — 43 passed (tests updated for local-time behavior)
- Website `npm test` — 3 passed
- `Verify-Safety.ps1` — Passed
- `git diff --check` — Clean

Branch pushed to origin: `ai/hermes/timezone-fix`
PR available at: https://github.com/marquezinii/FiveMCleaner/pull/new/ai/hermes/timezone-fix

---

## Risk Assessment

| Change | Risk Level | Reason |
|--------|------------|--------|
| Progress timestamps to UTC | Low | Internal timestamps already UTC; UI can convert for display |
| Dashboard local time conversion | Medium | User-facing; test with multiple timezones |
| Redundant call removal | None | Pure no-op removal |
| Date filter hint | None | Documentation only |

---

## Conclusion

The codebase is **well-designed for timezone correctness** with a clear UTC-first architecture. The issues found are primarily **consistency improvements** and **one user-facing UX gap** (dashboard showing UTC without indication). No correctness bugs or data corruption risks were identified.

**Priority order for fixes:**
1. Dashboard timestamp display (user-facing)
2. `AppOptimizationService` consistency (internal hygiene)
3. Redundant call removal (code clarity)
4. Dashboard date filter hint (UX polish)