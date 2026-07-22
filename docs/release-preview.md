# Release preview, integridade e simulação

Este documento descreve a distribuição de **FiveMCleaner v0.2.0-preview**. A
preview existe para validação pública; ela não deve ser tratada como uma versão
final ou como garantia de ganho de desempenho.

## Origem oficial

Baixe binários somente pela página
[GitHub Releases](https://github.com/marquezinii/FiveMCleaner/releases). Para a
preview `win-x64`, a publicação deve conter estes dois arquivos produzidos pelo
mesmo workflow:

- `FiveMCleaner-win-x64.zip`;
- `FiveMCleaner-win-x64.zip.sha256`.

Não use cópias hospedadas em encurtadores, mirrors, vídeos ou pacotes de
"FPS boost". O código-fonte correspondente deve estar disponível no mesmo tag
da release.

## Verificação SHA-256

Depois de baixar os dois arquivos para a mesma pasta, execute:

```powershell
$archive = Resolve-Path .\FiveMCleaner-win-x64.zip
$expected = ((Get-Content "$archive.sha256" -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()

if ($actual -ne $expected) {
    throw "SHA-256 divergente. Não execute este arquivo."
}

"SHA-256 confirmado: $actual"
```

O hash detecta corrupção e troca de arquivo. Como a preview ainda não possui
assinatura de código pública, o hash sozinho não substitui identidade do
publicador: confira também o domínio `github.com`, o repositório, o tag e o
código-fonte associado.

## Build ainda não assinado

Os executáveis desta preview são **unsigned**. Windows SmartScreen e produtos
antivírus podem, legitimamente, pedir confirmação ou bloquear um arquivo sem
reputação. Isso não deve ser contornado automaticamente.

O projeto nunca orienta o usuário a:

- desativar Defender, SmartScreen, firewall, UAC ou antivírus de terceiros;
- criar exclusão para a pasta ou executável;
- renomear, reempacotar ou ofuscar o binário para escapar de detecção;
- baixar uma cópia alternativa para evitar um alerta;
- executar um arquivo cujo hash diverge.

Se a política da máquina bloquear a preview, a opção segura é não executá-la,
revisar/compilar o código-fonte ou aguardar uma release assinada. Um falso
positivo reproduzível pode ser relatado com produto, versão das assinaturas e
SHA-256, sem enviar arquivos pessoais a serviços externos.

## Atalho de simulação para desenvolvimento

O script `scripts/Install-DevelopmentShortcut.ps1`, disponível no checkout do
repositório e não no ZIP portátil, cria na Área de Trabalho um atalho para o
caminho estável do build `Release` dentro deste workspace. O atalho aponta
diretamente para o executável WPF e usa `--demo`; ele não usa PowerShell, CMD ou
`dotnet run` ao ser aberto, portanto não cria janela de console.

```powershell
# Compila Release e instala/atualiza o atalho.
.\scripts\Install-DevelopmentShortcut.ps1 -Build

# Se o build Release já existe, somente instala/atualiza o atalho.
.\scripts\Install-DevelopmentShortcut.ps1
```

O modo `--demo` usa diagnóstico e histórico fictícios e não executa o plano de
otimização nem salva as opções da simulação. A tela de relato de bug continua
podendo acessar a rede **somente depois de um clique explícito em Enviar**.

O `.lnk` não contém uma cópia congelada do aplicativo. Cada nova build Release
substitui o executável no mesmo destino, e o atalho passa a abrir esse build. Se
`bin/` for limpo ou o workspace for movido, execute novamente o script com
`-Build`.
