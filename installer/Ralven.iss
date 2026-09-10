#define AppName "Ralven"
#define AppPublisher "Ralven"
#define AppPublisherUrl "https://vemryx.com/"
#define AppUrl "https://vemryx.com/Ralven/"
#define AppWebsite "https://vemryx.com/Ralven/"
#define AppExeName "Ralven.Launcher.exe"
#define AppUserModelId "Ralven.Ralven"
#define StableAppId "{{35FF816F-9EFD-42C8-A63B-CC5EA138805A}"

#ifndef AppVersion
  #define AppVersion "1.5.1"
#endif

#ifndef AppNumericVersion
  #define AppNumericVersion "1.5.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\Ralven-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef RepositoryRoot
  #define RepositoryRoot ".."
#endif

#ifndef InstallerArtworkPath
  #define InstallerArtworkPath "..\artifacts\installer-artwork\Ralven-wizard-side-light.png"
#endif

#ifndef InstallerArtworkPathDark
  #define InstallerArtworkPathDark "..\artifacts\installer-artwork\Ralven-wizard-side-dark.png"
#endif

#define InstallerBaseName "Ralven-Setup-" + AppVersion + "-win-x64"

[Setup]
AppId={#StableAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherUrl}
AppSupportURL={#AppWebsite}
AppUpdatesURL={#AppUrl}/releases/latest
AppCopyright=Copyright (c) 2026 Ralven. All rights reserved.
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
SetupIconFile={#RepositoryRoot}\src\Ralven.App\Assets\Ralven.ico
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
SetupMutex=Ralven.Setup.35FF816F-9EFD-42C8-A63B-CC5EA138805A
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
WizardSmallImageFile={#RepositoryRoot}\src\Ralven.App\Assets\Ralven.png
WizardSmallImageFileDynamicDark={#RepositoryRoot}\src\Ralven.App\Assets\Ralven.png
VersionInfoVersion={#AppNumericVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 Ralven. All rights reserved.
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
en.StartWithWindows=Start Ralven when I sign in to Windows
en.LaunchProgram=Open Ralven
en.UninstallShortcut=Uninstall Ralven
en.RemoveUserDataQuestion=Remove local Ralven settings, logs, backups and downloaded updates too? Choosing No preserves this data for a future installation.
ptbr.AdditionalShortcuts=Atalhos
ptbr.DesktopIcon=Criar um atalho na Área de Trabalho
ptbr.StartWithWindows=Iniciar o Ralven ao entrar no Windows
ptbr.LaunchProgram=Abrir o Ralven
ptbr.UninstallShortcut=Desinstalar o Ralven
ptbr.RemoveUserDataQuestion=Também remover configurações, logs, backups e atualizações baixadas do Ralven? Escolher Não preserva esses dados para uma instalação futura.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalShortcuts}:"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:AdditionalShortcuts}:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{#AppName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"; Comment: "{cm:UninstallShortcut}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{#AppName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ralven"; ValueData: """{app}\{#AppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startup; Check: not IsAutomaticUpdateRelaunch
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Ralven"; Flags: deletevalue uninsdeletevalue; Tasks: not startup; Check: not IsAutomaticUpdateRelaunch

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
; The app's own updater relaunches this installer once after a successful
; Ralven download. Without /AUTOUPDATE=yes (any other silent install, including an admin
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
    DelTree(ExpandConstant('{localappdata}\Ralven'), True, True, True);
end;
