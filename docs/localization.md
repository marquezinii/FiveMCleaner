# Localização

O Ralven mantém traduções versionadas e funciona offline. `localization/locales.json` é o catálogo de idiomas e conjuntos de recursos; adicionar um idioma exige somente uma entrada nesse arquivo e os catálogos gerados, sem alterar o seletor ou a lógica da aplicação.

## Fonte canônica

- `en-US` é o idioma-base.
- Os recursos do aplicativo ficam em `src/Ralven.App/Resources/Strings*.resx`.
- Os recursos do atualizador ficam em `src/Ralven.Updater/Resources/Strings*.resx`.
- Os resultados de diagnóstico e manutenção da camada Windows ficam em `src/Ralven.Windows/Resources/Strings*.resx`; o app e o broker injetam o locale selecionado no mesmo resolver.
- `localization/glossary.json` contém marcas e termos técnicos protegidos.
- `localization/review-state.json` registra o hash revisado de cada texto-base.
- Uma chave pública deve existir em todos os idiomas, com os mesmos placeholders de formato.

## Fluxo de tradução

Depois de adicionar ou alterar textos no catálogo-base:

```powershell
./scripts/Sync-Localization.ps1 -Mode Sync
```

O comando cria catálogos ausentes, acrescenta novas chaves e marca cada novo valor como `TODO(i18n)`. Também exporta `artifacts/localization/translation-drafts.json`, que pode ser entregue a uma ferramenta de tradução offline ou a um serviço escolhido pelo revisor. A aplicação nunca traduz em runtime.

Após preencher o campo `translation` de cada rascunho:

```powershell
./scripts/Sync-Localization.ps1 -Mode Import
./scripts/Sync-Localization.ps1 -Mode Approve
./scripts/Sync-Localization.ps1 -Mode Check
```

A importação recusa valores vazios e placeholders alterados. Depois da revisão humana, `Approve` atualiza os hashes por chave; assim, alterar um texto-base existente sem revisar as traduções também quebra o CI. A verificação recusa catálogos ou chaves ausentes, chaves duplicadas ou órfãs, rascunhos sem revisão, fontes alteradas sem aprovação, placeholders divergentes e alterações em termos protegidos pelo glossário.

Tradução automática é somente um rascunho. Textos de segurança, privacidade, conta, atualização, ações e limitações de risco exigem revisão humana contextual antes do commit.

## Pseudo-localização e inspeção visual

A cultura de desenvolvimento `qps-Ploc` expande e acentua as strings sem alterar placeholders. Ela não aparece no seletor público e pode ser usada no modo de captura:

```powershell
./scripts/Test-LocalizationVisual.ps1 -IncludePseudo
```

O script captura todas as páginas de demonstração em todos os idiomas declarados no catálogo. As imagens ficam em `artifacts/localization/visual` para revisar cortes, sobreposição e perda de conteúdo. A pseudo-localização é um teste de layout, não um idioma distribuído ao usuário.
