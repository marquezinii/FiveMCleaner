#define AppName "FiveMCleaner"
#define AppPublisher "Felipe Marquezini"
#define AppUrl "https://github.com/marquezinii/FiveMCleaner"
#define AppWebsite "https://marquezinii.github.io/FiveMCleaner/"
#define AppExeName "FiveMCleaner.Launcher.exe"
#define StableAppId "{{49338651-127F-4FD3-BEAD-88D8C9377672}"

#ifndef AppVersion
  #define AppVersion "1.4.0"
#endif

#ifndef AppNumericVersion
  #define AppNumericVersion "1.4.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\FiveMCleaner-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef RepositoryRoot
  #define RepositoryRoot ".."
#endif

#ifndef InstallerArtworkPath
  #define InstallerArtworkPath "..\artifacts\installer-artwork\FiveMCleaner-wizard-side-light.png"
#endif

#ifndef InstallerArtworkPathDark
  #define InstallerArtworkPathDark "..\artifacts\installer-artwork\FiveMCleaner-wizard-side-dark.png"
#endif

#define InstallerBaseName "FiveMCleaner-Setup-" + AppVersion + "-win-x64"

[Setup]
AppId={#StableAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppWebsite}
AppUpdatesURL={#AppUrl}/releases/latest
AppCopyright=Copyright (c) 2026 Felipe Marquezini. All rights reserved.
AppComments=Transparent and reversible optimization for FiveM and Windows.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
AllowNetworkDrive=no
AllowUNCPath=no
DisableWelcomePage=no
DisableProgramGroupPage=auto
DisableDirPage=auto
DisableReadyPage=no
LicenseFile={#RepositoryRoot}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename={#InstallerBaseName}
OutputManifestFile={#InstallerBaseName}.contents.txt
SetupIconFile={#RepositoryRoot}\src\FiveMCleaner.App\Assets\FiveMCleaner.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
UninstallFilesDir={app}
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
RestartIfNeededByRun=no
RedirectionGuard=yes
ASLRCompatible=yes
DEPCompatible=yes
Compression=lzma2/ultra
SolidCompression=yes
MergeDuplicateFiles=yes
TimeStampsInUTC=yes
SetupLogging=yes
SetupMutex=FiveMCleaner.Setup.49338651-127F-4FD3-BEAD-88D8C9377672
UninstallLogging=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousLanguage=no
UsePreviousTasks=yes
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=no
WizardStyle=modern dynamic windows11 includetitlebar
WizardResizable=yes
WizardKeepAspectRatio=yes
WizardImageFile={#InstallerArtworkPath}
WizardImageFileDynamicDark={#InstallerArtworkPathDark}
WizardImageBackColor=$F3F4F6
WizardImageBackColorDynamicDark=$151515
WizardSmallImageFile={#RepositoryRoot}\src\FiveMCleaner.App\Assets\FiveMCleaner.png
WizardSmallImageFileDynamicDark={#RepositoryRoot}\src\FiveMCleaner.App\Assets\FiveMCleaner.png
VersionInfoVersion={#AppNumericVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 Felipe Marquezini. All rights reserved.
VersionInfoOriginalFileName={#InstallerBaseName}.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "{#RepositoryRoot}\installer\install-info.en.txt"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "{#RepositoryRoot}\installer\install-info.pt-BR.txt"

[Messages]
en.FinishedLabel=Setup has finished installing [name] on your computer.%n%nLater updates are delivered inside the app after you confirm them. You usually do not need to download this installer again.%n%nSetup logs are written under your temporary folder when logging is enabled.
ptbr.FinishedLabel=A instalação do [name] foi concluída.%n%nAtualizações futuras chegam pelo próprio aplicativo, após a sua confirmação. Em geral não é necessário baixar este instalador de novo.%n%nQuando o log estiver ativo, os arquivos ficam na pasta temporária do Windows.

[CustomMessages]
en.AdditionalShortcuts=Shortcuts
en.DesktopIcon=Create a desktop shortcut
en.StartWithWindows=Start FiveMCleaner when I sign in to Windows
en.LaunchProgram=Open FiveMCleaner
en.UninstallShortcut=Uninstall FiveMCleaner
en.RemoveUserDataQuestion=Remove local FiveMCleaner settings, logs, backups and downloaded updates too? Choosing No preserves this data for a future installation.
ptbr.AdditionalShortcuts=Atalhos
ptbr.DesktopIcon=Criar um atalho na Área de Trabalho
ptbr.StartWithWindows=Iniciar o FiveMCleaner ao entrar no Windows
ptbr.LaunchProgram=Abrir o FiveMCleaner
ptbr.UninstallShortcut=Desinstalar o FiveMCleaner
ptbr.RemoveUserDataQuestion=Também remover configurações, logs, backups e atualizações baixadas do FiveMCleaner? Escolher Não preserva esses dados para uma instalação futura.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalShortcuts}:"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalShortcuts}:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Comment: "{#AppName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"; Comment: "{cm:UninstallShortcut}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Comment: "{#AppName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FiveMCleaner"; ValueData: """{app}\{#AppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FiveMCleaner"; Flags: deletevalue uninsdeletevalue; Tasks: not startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
; Migration update only. Existing installations launch this installer once;
; it installs the immutable runtime layout and starts the stable launcher.
; Without /AUTOUPDATE=yes (any other silent install, including an admin
; deployment) nothing is launched, which is the expected silent behavior.
Filename: "{app}\{#AppExeName}"; Parameters: "--updated={#AppVersion}"; WorkingDir: "{app}"; Flags: nowait runasoriginaluser; Check: IsAutomaticUpdateRelaunch

[UninstallDelete]
Type: dirifempty; Name: "{app}\Runtime\versions"
Type: dirifempty; Name: "{app}\Runtime"
Type: dirifempty; Name: "{app}"

[Code]
var
  RemoveUserData: Boolean;

{ True only for the app's own one-click update: a silent run explicitly
  started with /AUTOUPDATE=yes. Both conditions are required so that a plain
  /VERYSILENT deployment never gets an unexpected application launch. }
function IsAutomaticUpdateRelaunch(): Boolean;
begin
  Result := WizardSilent and
    (CompareText(ExpandConstant('{param:AUTOUPDATE|no}'), 'yes') = 0);
end;

function InitializeUninstall(): Boolean;
begin
  RemoveUserData := SuppressibleMsgBox(
    CustomMessage('RemoveUserDataQuestion'),
    mbConfirmation,
    MB_YESNO,
    IDNO) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    DelTree(ExpandConstant('{localappdata}\FiveMCleaner'), True, True, True);
end;
