# Error message improvements

- Agent: Codex
- Branch: `ai/codex/error-message-improvements`
- Status: ready for integration
- Objective: make user-facing failures actionable, localized, and free of raw technical exception details.

## Changes

- Added shared exception-to-message mapping for English, Portuguese, and Spanish.
- Applied it to diagnostics, optimization, rollback, update, report, clipboard, bug-report, fatal-dialog, and external-link flows.
- Replaced raw launcher/updater exception dialogs with specific recovery guidance.

## Validation

- `dotnet test FiveMCleaner.slnx -c Release --no-restore` passed.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` passed.
- `scripts\Verify-Safety.ps1` passed.
- `git diff --check` passed.

## Commit

- One local commit: `fix: improve user-facing error messages`.
