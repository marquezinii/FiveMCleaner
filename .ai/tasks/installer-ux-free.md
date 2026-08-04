# installer-ux-free

- **Agente:** opencode
- **Branch:** `ai/opencode/installer-ux`
- **Worktree:** `C:\Projetos\FiveMCleaner-ai-opencode-installer-ux`
- **Objetivo:** aplicar melhorias gratuitas do instalador (sem Authenticode/certificado)
- **Status:** pronto para integração

## Mudanças

- Startup com Windows **desmarcado** por padrão; desktop permanece marcado
- i18n: atalho Desinstalar, FinishedLabel EN/PT-BR; `AppComments`/VersionInfo em inglês
- Artwork light + dark (`New-InstallerArtwork.ps1` + defines no `.iss`)
- `lzma2/ultra`
- `install-info` EN/PT com site, LOCALAPPDATA, SHA-256 e política de tasks
- `Verify-Installer` + `Test-Installer` (defaults, AUTOUPDATE, preservação de dados)
- CI recusa worktree suja no `Build-Installer` (`GITHUB_ACTIONS`/`CI`); `-AllowDirtySource` override
- `docs/installer.md` atualizado
- `AppSupportURL` → site público

## Fora de escopo (pago / certificado)

- Assinatura Authenticode do Setup
- ARM64 multi-arch

## Testes

- `Verify-Installer.ps1 -ScriptOnly` — OK
- `New-InstallerArtwork.ps1` light/dark — OK
- `Build-Installer.ps1 -AllowDirtySource` — OK (artefato gerado, contract artifact OK)
- `Test-Installer.ps1` — **não executado**: instalação real de FiveMCleaner presente na máquina (guarda intencional). CI/release limpo deve rodar o smoke.

## Commits

(ver log da branch após commit)
