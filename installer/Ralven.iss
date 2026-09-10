#define AppName "Ralven"
#define AppPublisher "Ralven"
#define AppPublisherUrl "https://vemryx.com/"
#define AppUrl "https://vemryx.com/Ralven/"
#define AppWebsite "https://vemryx.com/Ralven/"
#define AppExeName "Ralven.Launcher.exe"
#define AppUserModelId "Ralven.Ralven"
#define StableAppId "{{35FF816F-9EFD-42C8-A63B-CC5EA138805A}"

#ifndef AppVersion
  #error AppVersion must be provided by scripts/Build-Installer.ps1
#endif

#ifndef AppNumericVersion
  #error AppNumericVersion must be provided by scripts/Build-Installer.ps1
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

#ifndef InstallerLicensePath
  #define InstallerLicensePath "..\artifacts\installer-documents\license.rtf"
#endif

#ifndef InstallerInfoEnglishPath
  #define InstallerInfoEnglishPath "..\artifacts\installer-documents\install-info.en.rtf"
#endif

#ifndef InstallerInfoPortuguesePath
  #define InstallerInfoPortuguesePath "..\artifacts\installer-documents\install-info.pt-BR.rtf"
#endif

#ifndef InstallerInfoSpanishPath
  #define InstallerInfoSpanishPath "..\artifacts\installer-documents\install-info.es.rtf"
#endif

#ifndef InstallerInfoFrenchPath
  #define InstallerInfoFrenchPath "..\artifacts\installer-documents\install-info.fr.rtf"
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
AppComments=Transparent and reversible Windows management for diagnostics, maintenance and optimization.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
AllowNetworkDrive=no
AllowUNCPath=no
DisableWelcomePage=no
DisableProgramGroupPage=auto
DisableDirPage=auto
DisableReadyPage=no
LicenseFile={#InstallerLicensePath}
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
WizardSizePercent=140,135
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

[LangOptions]
DialogFontName=Segoe UI
DialogFontSize=10

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "{#InstallerInfoEnglishPath}"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "{#InstallerInfoPortuguesePath}"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "{#InstallerInfoSpanishPath}"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"; InfoBeforeFile: "{#InstallerInfoFrenchPath}"

[Messages]
en.ButtonBack=Back
en.ButtonNext=Next
en.ButtonInstall=Install
en.ButtonFinish=Finish
en.ButtonBrowse=Browse...
en.ButtonWizardBrowse=Browse...
en.ButtonNewFolder=Create folder
en.ButtonYes=Yes
en.ButtonNo=No
en.LicenseAccepted=I accept the agreement
en.LicenseNotAccepted=I do not accept the agreement
en.FinishedLabel=Setup has finished installing [name] on your computer.%n%nLater updates are delivered inside the app after you confirm them. You usually do not need to download this installer again.%n%nSetup logs are written under your temporary folder when logging is enabled.
ptbr.ButtonBack=Voltar
ptbr.ButtonNext=Avançar
ptbr.ButtonInstall=Instalar
ptbr.ButtonFinish=Concluir
ptbr.ButtonBrowse=Procurar...
ptbr.ButtonWizardBrowse=Procurar...
ptbr.ButtonNewFolder=Criar pasta
ptbr.ButtonYes=Sim
ptbr.ButtonNo=Não
ptbr.LicenseAccepted=Eu aceito o acordo
ptbr.LicenseNotAccepted=Eu não aceito o acordo
ptbr.FinishedLabel=A instalação do [name] foi concluída.%n%nAtualizações futuras chegam pelo próprio aplicativo, após a sua confirmação. Em geral não é necessário baixar este instalador de novo.%n%nQuando o log estiver ativo, os arquivos ficam na pasta temporária do Windows.
es.FinishedLabel=La instalación de [name] ha finalizado.%n%nLas próximas actualizaciones se ofrecen en la aplicación después de tu confirmación. Normalmente no tendrás que descargar este instalador de nuevo.%n%nCuando el registro está activado, los archivos se guardan en la carpeta temporal de Windows.
fr.FinishedLabel=L'installation de [name] est terminée.%n%nLes prochaines mises à jour sont proposées dans l'application après votre confirmation. Vous n'aurez normalement pas besoin de télécharger à nouveau ce programme d'installation.%n%nLorsque la journalisation est activée, les fichiers sont enregistrés dans le dossier temporaire de Windows.

[CustomMessages]
en.AdditionalShortcuts=Shortcuts
en.DesktopIcon=Create a desktop shortcut
en.StartWithWindowsTask=Start Ralven when I sign in to Windows
en.LaunchProgram=Open Ralven
en.UninstallShortcut=Uninstall Ralven
en.RemoveUserDataQuestion=Remove local Ralven settings, logs, backups and downloaded updates too? Choosing No preserves this data for a future installation.
en.WelcomeTitle=Ralven
en.WelcomeSubtitle=Set up transparent and reversible Windows management for diagnostics, maintenance and optimization.
en.WelcomeBody=Installation works offline, does not require administrator access and preserves your existing local data.
en.InstallAction=Install
en.LicenseIntro=Read the complete license before continuing. Headings, lists and emphasis are formatted for comfortable reading.
en.InfoIntro=Review how Ralven is installed, updated and removed on this PC.
en.ProgressTitle=Installing Ralven
en.ProgressSubtitle=You can keep this window open while Setup completes the steps below.
en.ProgressSummary=%1 of 4 steps complete
en.StepPreparing=Prepare installation
en.StepFiles=Install Ralven files
en.StepPreferences=Create shortcuts and preferences
en.StepFinishing=Finish and verify installation
en.StatePending=Pending
en.StateActive=In progress
en.StateComplete=Complete
en.StateFailed=Failed
en.ShowDetails=Show details
en.HideDetails=Hide details
en.TechnicalDetails=Technical details
en.LogStarted=Setup started.
en.LogDestination=Destination: %1
en.LogPreparing=Preparing the installation.
en.LogInstalling=Installing the verified Ralven package.
en.LogProgress=Installation progress: %1%%
en.LogFile=Writing: %1
en.LogSelectedTasks=Selected options: %1
en.LogPreferences=Creating the selected shortcuts and preferences.
en.LogFinishing=Finalizing the installation.
en.LogCompleted=Installation completed successfully.
en.CompletedTitle=Ralven is ready
en.CompletedBody=The application was installed for your Windows account. Future updates are offered inside Ralven after your confirmation.
ptbr.AdditionalShortcuts=Atalhos
ptbr.DesktopIcon=Criar um atalho na Área de Trabalho
ptbr.StartWithWindowsTask=Iniciar o Ralven ao entrar no Windows
ptbr.LaunchProgram=Abrir o Ralven
ptbr.UninstallShortcut=Desinstalar o Ralven
ptbr.RemoveUserDataQuestion=Também remover configurações, logs, backups e atualizações baixadas do Ralven? Escolher Não preserva esses dados para uma instalação futura.
ptbr.WelcomeTitle=Ralven
ptbr.WelcomeSubtitle=Configure o gerenciamento transparente e reversível do Windows para diagnóstico, manutenção e otimização.
ptbr.WelcomeBody=A instalação funciona offline, não exige acesso de administrador e preserva seus dados locais existentes.
ptbr.InstallAction=Instalar
ptbr.LicenseIntro=Leia a licença completa antes de continuar. Títulos, listas e destaques estão formatados para uma leitura confortável.
ptbr.InfoIntro=Confira como o Ralven será instalado, atualizado e removido deste computador.
ptbr.ProgressTitle=Instalando o Ralven
ptbr.ProgressSubtitle=Você pode manter esta janela aberta enquanto o instalador conclui as etapas abaixo.
ptbr.ProgressSummary=%1 de 4 etapas concluídas
ptbr.StepPreparing=Preparar a instalação
ptbr.StepFiles=Instalar os arquivos do Ralven
ptbr.StepPreferences=Criar atalhos e preferências
ptbr.StepFinishing=Concluir e verificar a instalação
ptbr.StatePending=Pendente
ptbr.StateActive=Em andamento
ptbr.StateComplete=Concluída
ptbr.StateFailed=Falhou
ptbr.ShowDetails=Mostrar detalhes
ptbr.HideDetails=Ocultar detalhes
ptbr.TechnicalDetails=Detalhes técnicos
ptbr.LogStarted=Instalador iniciado.
ptbr.LogDestination=Destino: %1
ptbr.LogPreparing=Preparando a instalação.
ptbr.LogInstalling=Instalando o pacote verificado do Ralven.
ptbr.LogProgress=Progresso da instalação: %1%%
ptbr.LogFile=Gravando: %1
ptbr.LogSelectedTasks=Opções selecionadas: %1
ptbr.LogPreferences=Criando os atalhos e as preferências selecionadas.
ptbr.LogFinishing=Finalizando a instalação.
ptbr.LogCompleted=Instalação concluída com sucesso.
ptbr.CompletedTitle=O Ralven está pronto
ptbr.CompletedBody=O aplicativo foi instalado para a sua conta do Windows. Atualizações futuras serão oferecidas dentro do Ralven após a sua confirmação.
es.AdditionalShortcuts=Accesos directos
es.DesktopIcon=Crear un acceso directo en el escritorio
es.StartWithWindowsTask=Iniciar Ralven al iniciar sesión en Windows
es.LaunchProgram=Abrir Ralven
es.UninstallShortcut=Desinstalar Ralven
es.RemoveUserDataQuestion=¿También quieres eliminar la configuración local, los registros, las copias de seguridad y las actualizaciones descargadas de Ralven? Si eliges No, estos datos se conservarán para una instalación futura.
es.WelcomeTitle=Ralven
es.WelcomeSubtitle=Configura una gestión de Windows transparente y reversible para diagnóstico, mantenimiento y optimización.
es.WelcomeBody=La instalación funciona sin conexión, no requiere acceso de administrador y conserva tus datos locales existentes.
es.InstallAction=Instalar
es.LicenseIntro=Lee la licencia completa antes de continuar. Los títulos, listas y destacados están formateados para una lectura cómoda.
es.InfoIntro=Revisa cómo se instala, actualiza y elimina Ralven de este equipo.
es.ProgressTitle=Instalando Ralven
es.ProgressSubtitle=Puedes mantener esta ventana abierta mientras el instalador completa los pasos siguientes.
es.ProgressSummary=%1 de 4 pasos completados
es.StepPreparing=Preparar la instalación
es.StepFiles=Instalar los archivos de Ralven
es.StepPreferences=Crear accesos directos y preferencias
es.StepFinishing=Finalizar y verificar la instalación
es.StatePending=Pendiente
es.StateActive=En curso
es.StateComplete=Completado
es.StateFailed=Fallido
es.ShowDetails=Mostrar detalles
es.HideDetails=Ocultar detalles
es.TechnicalDetails=Detalles técnicos
es.LogStarted=Instalador iniciado.
es.LogDestination=Destino: %1
es.LogPreparing=Preparando la instalación.
es.LogInstalling=Instalando el paquete verificado de Ralven.
es.LogProgress=Progreso de instalación: %1%%
es.LogFile=Escribiendo: %1
es.LogSelectedTasks=Opciones seleccionadas: %1
es.LogPreferences=Creando los accesos directos y preferencias seleccionados.
es.LogFinishing=Finalizando la instalación.
es.LogCompleted=Instalación completada correctamente.
es.CompletedTitle=Ralven está listo
es.CompletedBody=La aplicación se instaló para tu cuenta de Windows. Las actualizaciones futuras se ofrecerán dentro de Ralven después de tu confirmación.
fr.AdditionalShortcuts=Raccourcis
fr.DesktopIcon=Créer un raccourci sur le Bureau
fr.StartWithWindowsTask=Démarrer Ralven à l'ouverture de session Windows
fr.LaunchProgram=Ouvrir Ralven
fr.UninstallShortcut=Désinstaller Ralven
fr.RemoveUserDataQuestion=Supprimer également les paramètres locaux, les journaux, les sauvegardes et les mises à jour téléchargées de Ralven ? Si vous choisissez Non, ces données seront conservées pour une prochaine installation.
fr.WelcomeTitle=Ralven
fr.WelcomeSubtitle=Configurez une gestion transparente et réversible de Windows pour le diagnostic, la maintenance et l’optimisation.
fr.WelcomeBody=L’installation fonctionne hors ligne, ne demande pas d’accès administrateur et préserve vos données locales existantes.
fr.InstallAction=Installer
fr.LicenseIntro=Lisez la licence complète avant de continuer. Les titres, listes et mises en évidence sont formatés pour une lecture confortable.
fr.InfoIntro=Découvrez comment Ralven est installé, mis à jour et supprimé de ce PC.
fr.ProgressTitle=Installation de Ralven
fr.ProgressSubtitle=Vous pouvez laisser cette fenêtre ouverte pendant que l’installateur termine les étapes ci-dessous.
fr.ProgressSummary=%1 étapes terminées sur 4
fr.StepPreparing=Préparer l’installation
fr.StepFiles=Installer les fichiers de Ralven
fr.StepPreferences=Créer les raccourcis et préférences
fr.StepFinishing=Terminer et vérifier l’installation
fr.StatePending=En attente
fr.StateActive=En cours
fr.StateComplete=Terminé
fr.StateFailed=Échec
fr.ShowDetails=Afficher les détails
fr.HideDetails=Masquer les détails
fr.TechnicalDetails=Détails techniques
fr.LogStarted=Installateur démarré.
fr.LogDestination=Destination : %1
fr.LogPreparing=Préparation de l’installation.
fr.LogInstalling=Installation du package Ralven vérifié.
fr.LogProgress=Progression de l’installation : %1%%
fr.LogFile=Écriture : %1
fr.LogSelectedTasks=Options sélectionnées : %1
fr.LogPreferences=Création des raccourcis et préférences sélectionnés.
fr.LogFinishing=Finalisation de l’installation.
fr.LogCompleted=Installation terminée avec succès.
fr.CompletedTitle=Ralven est prêt
fr.CompletedBody=L’application a été installée pour votre compte Windows. Les prochaines mises à jour seront proposées dans Ralven après votre confirmation.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalShortcuts}:"
Name: "startup"; Description: "{cm:StartWithWindowsTask}"; GroupDescription: "{cm:AdditionalShortcuts}:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs notimestamp

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{#AppName}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"; Comment: "{cm:UninstallShortcut}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; AppUserModelID: "{#AppUserModelId}"; Comment: "{#AppName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ralven"; ValueData: """{app}\{#AppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startup; Check: not IsAutomaticUpdateRelaunch; BeforeInstall: BeginShellConfiguration
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Ralven"; Flags: deletevalue uninsdeletevalue; Tasks: not startup; Check: not IsAutomaticUpdateRelaunch; BeforeInstall: BeginShellConfiguration

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
type
  TInstallerStepState = (issPending, issActive, issComplete, issFailed);

var
  RemoveUserData: Boolean;
  WelcomeBrandImage: TBitmapImage;
  FinishedBrandImage: TBitmapImage;
  ProgressSummaryLabel: TNewStaticText;
  ProgressActivityLabel: TNewStaticText;
  ProgressStepLabels: array[0..3] of TNewStaticText;
  ProgressStepNames: array[0..3] of String;
  ProgressStepStates: array[0..3] of TInstallerStepState;
  TechnicalDetailsTitle: TNewStaticText;
  TechnicalDetailsMemo: TNewMemo;
  TechnicalDetailsButton: TNewButton;
  TechnicalDetailsVisible: Boolean;
  CurrentInstallStage: Integer;
  LastLoggedProgressBucket: Integer;
  LastProgressPercent: Integer;
  LastNativeStatus: String;
  LastNativeFilename: String;

function InstallerTimestamp(): String;
begin
  Result := GetDateTimeString('hh:nn:ss', '-', ':');
end;

function WithoutMnemonic(const Caption: String): String;
begin
  Result := Caption;
  StringChangeEx(Result, '&', '', True);
end;

procedure AppendTechnicalDetail(const Detail: String);
begin
  TechnicalDetailsMemo.Lines.Add('[' + InstallerTimestamp() + '] ' + Detail);
  TechnicalDetailsMemo.SelStart := Length(TechnicalDetailsMemo.Text);
  TechnicalDetailsMemo.SelLength := 0;
end;

function StepStateText(const State: TInstallerStepState): String;
begin
  case State of
    issActive: Result := CustomMessage('StateActive');
    issComplete: Result := CustomMessage('StateComplete');
    issFailed: Result := CustomMessage('StateFailed');
  else
    Result := CustomMessage('StatePending');
  end;
end;

function StepStateSymbol(const State: TInstallerStepState): String;
begin
  case State of
    issActive: Result := '●';
    issComplete: Result := '✓';
    issFailed: Result := '!';
  else
    Result := '○';
  end;
end;

procedure UpdateProgressSummary;
var
  Index: Integer;
  Completed: Integer;
begin
  Completed := 0;
  for Index := 0 to 3 do
    if ProgressStepStates[Index] = issComplete then
      Completed := Completed + 1;

  ProgressSummaryLabel.Caption := FmtMessage(
    CustomMessage('ProgressSummary'), [IntToStr(Completed)]);
  if LastProgressPercent > 0 then
    ProgressSummaryLabel.Caption := ProgressSummaryLabel.Caption +
      '  ·  ' + IntToStr(LastProgressPercent) + '%';
end;

procedure UpdateStepLabel(const Index: Integer);
begin
  ProgressStepLabels[Index].Caption := StepStateSymbol(ProgressStepStates[Index]) +
    '  ' + ProgressStepNames[Index] + '  —  ' + StepStateText(ProgressStepStates[Index]);
  ProgressStepLabels[Index].Font.Color := clWindowText;
  ProgressStepLabels[Index].Font.Style := [];

  if ProgressStepStates[Index] = issPending then
    ProgressStepLabels[Index].Font.Color := clGrayText
  else if ProgressStepStates[Index] in [issActive, issFailed] then
    ProgressStepLabels[Index].Font.Style := [fsBold];
end;

procedure SetStepState(const Index: Integer; const State: TInstallerStepState);
begin
  ProgressStepStates[Index] := State;
  UpdateStepLabel(Index);
  UpdateProgressSummary;
end;

procedure LayoutWelcomePage;
var
  PageWidth: Integer;
  LogoSize: Integer;
  Margin: Integer;
begin
  WizardForm.WelcomePage.SetBounds(0, 0,
    WizardForm.MainPanel.ClientWidth, WizardForm.MainPanel.ClientHeight);
  PageWidth := WizardForm.WelcomePage.ClientWidth;
  LogoSize := ScaleX(112);
  Margin := ScaleX(40);

  WelcomeBrandImage.SetBounds(
    (PageWidth - LogoSize) div 2, ScaleY(38), LogoSize, LogoSize);
  WizardForm.WelcomeLabel1.SetBounds(
    Margin, ScaleY(170), PageWidth - (2 * Margin), ScaleY(48));
  WizardForm.WelcomeLabel2.SetBounds(
    Margin, ScaleY(224), PageWidth - (2 * Margin), ScaleY(98));
end;

procedure LayoutFinishedPage;
var
  PageWidth: Integer;
  LogoSize: Integer;
  Margin: Integer;
  RunTop: Integer;
begin
  WizardForm.FinishedPage.SetBounds(0, 0,
    WizardForm.MainPanel.ClientWidth, WizardForm.MainPanel.ClientHeight);
  PageWidth := WizardForm.FinishedPage.ClientWidth;
  LogoSize := ScaleX(88);
  Margin := ScaleX(40);

  FinishedBrandImage.SetBounds(
    (PageWidth - LogoSize) div 2, ScaleY(28), LogoSize, LogoSize);
  WizardForm.FinishedHeadingLabel.SetBounds(
    Margin, ScaleY(130), PageWidth - (2 * Margin), ScaleY(44));
  WizardForm.FinishedLabel.SetBounds(
    Margin, ScaleY(180), PageWidth - (2 * Margin), ScaleY(160));
  WizardForm.FinishedLabel.AdjustHeight;
  RunTop := WizardForm.FinishedLabel.Top +
    WizardForm.FinishedLabel.Height + ScaleY(20);
  WizardForm.RunList.SetBounds(
    Margin, RunTop, PageWidth - (2 * Margin), ScaleY(64));
end;

procedure LayoutProgressPage;
var
  PageWidth: Integer;
  PageHeight: Integer;
  ContentWidth: Integer;
  Gap: Integer;
  Margin: Integer;
  StepTop: Integer;
  Index: Integer;
begin
  PageWidth := WizardForm.InstallingPage.ClientWidth;
  PageHeight := WizardForm.InstallingPage.ClientHeight;
  Gap := ScaleX(24);
  Margin := ScaleX(8);

  if TechnicalDetailsVisible then
    ContentWidth := (PageWidth - Gap) div 2
  else
    ContentWidth := PageWidth;

  ProgressSummaryLabel.SetBounds(Margin, ScaleY(10),
    ContentWidth - (2 * Margin), ScaleY(24));
  WizardForm.ProgressGauge.SetBounds(Margin, ScaleY(42),
    ContentWidth - (2 * Margin), ScaleY(12));

  ProgressActivityLabel.SetBounds(Margin, ScaleY(62),
    ContentWidth - (2 * Margin), ScaleY(24));

  StepTop := ScaleY(96);
  for Index := 0 to 3 do
    ProgressStepLabels[Index].SetBounds(Margin, StepTop + (Index * ScaleY(48)),
      ContentWidth - (2 * Margin), ScaleY(42));

  TechnicalDetailsButton.SetBounds(Margin, PageHeight - ScaleY(38),
    ScaleX(146), ScaleY(30));

  TechnicalDetailsTitle.Visible := TechnicalDetailsVisible;
  TechnicalDetailsMemo.Visible := TechnicalDetailsVisible;
  if TechnicalDetailsVisible then
  begin
    TechnicalDetailsTitle.SetBounds(ContentWidth + Gap, ScaleY(10),
      ContentWidth - Margin, ScaleY(24));
    TechnicalDetailsMemo.SetBounds(ContentWidth + Gap, ScaleY(42),
      ContentWidth - Gap - Margin, PageHeight - ScaleY(50));
  end;
end;

procedure LayoutInstaller(Sender: TObject);
begin
  LayoutWelcomePage;
  LayoutProgressPage;
  LayoutFinishedPage;
end;

procedure ToggleTechnicalDetails(Sender: TObject);
begin
  TechnicalDetailsVisible := not TechnicalDetailsVisible;
  if TechnicalDetailsVisible then
    TechnicalDetailsButton.Caption := CustomMessage('HideDetails')
  else
    TechnicalDetailsButton.Caption := CustomMessage('ShowDetails');
  LayoutProgressPage;
end;

procedure InitializeWelcomePage;
begin
  WizardForm.WizardBitmapImage.Visible := False;

  WelcomeBrandImage := TBitmapImage.Create(WizardForm);
  WelcomeBrandImage.Parent := WizardForm.WelcomePage;
  WelcomeBrandImage.Bitmap.Assign(WizardForm.WizardSmallBitmapImage.Bitmap);
  WelcomeBrandImage.Stretch := True;
  WelcomeBrandImage.Center := True;

  WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeTitle');
  WizardForm.WelcomeLabel1.AutoSize := False;
  WizardForm.WelcomeLabel1.Alignment := taCenter;
  WizardForm.WelcomeLabel1.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel1.Font.Size := 28;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];

  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeSubtitle') + #13#10 + #13#10 +
    CustomMessage('WelcomeBody');
  WizardForm.WelcomeLabel2.AutoSize := False;
  WizardForm.WelcomeLabel2.Alignment := taCenter;
  WizardForm.WelcomeLabel2.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel2.Font.Size := 11;
  WizardForm.WelcomeLabel2.WordWrap := True;
end;

procedure InitializeDocumentPages;
begin
  WizardForm.LicenseLabel1.Caption := CustomMessage('LicenseIntro');
  WizardForm.LicenseLabel1.Font.Size := 10;
  WizardForm.LicenseMemo.BorderStyle := bsNone;
  WizardForm.LicenseMemo.BevelKind := bkNone;
  WizardForm.LicenseMemo.ScrollBars := ssVertical;

  WizardForm.InfoBeforeClickLabel.Caption := CustomMessage('InfoIntro');
  WizardForm.InfoBeforeClickLabel.Font.Size := 10;
  WizardForm.InfoBeforeMemo.BorderStyle := bsNone;
  WizardForm.InfoBeforeMemo.BevelKind := bkNone;
  WizardForm.InfoBeforeMemo.ScrollBars := ssVertical;

  WizardForm.LicenseAcceptedRadio.Caption :=
    WithoutMnemonic(WizardForm.LicenseAcceptedRadio.Caption);
  WizardForm.LicenseNotAcceptedRadio.Caption :=
    WithoutMnemonic(WizardForm.LicenseNotAcceptedRadio.Caption);

  WizardForm.ReadyMemo.BorderStyle := bsNone;
  WizardForm.TasksList.BorderStyle := bsNone;
  WizardForm.TasksList.Flat := True;
  WizardForm.TasksList.ShowLines := False;
end;

procedure InitializeProgressPage;
var
  Index: Integer;
begin
  WizardForm.StatusLabel.Visible := False;
  WizardForm.FilenameLabel.Visible := False;
  WizardForm.ProgressGauge.Style := npbstNormal;

  ProgressSummaryLabel := TNewStaticText.Create(WizardForm);
  ProgressSummaryLabel.Parent := WizardForm.InstallingPage;
  ProgressSummaryLabel.AutoSize := False;
  ProgressSummaryLabel.Font.Size := 10;

  ProgressActivityLabel := TNewStaticText.Create(WizardForm);
  ProgressActivityLabel.Parent := WizardForm.InstallingPage;
  ProgressActivityLabel.AutoSize := False;
  ProgressActivityLabel.Font.Size := 9;
  ProgressActivityLabel.Font.Color := clGrayText;
  ProgressActivityLabel.Caption := CustomMessage('LogPreparing');

  ProgressStepNames[0] := CustomMessage('StepPreparing');
  ProgressStepNames[1] := CustomMessage('StepFiles');
  ProgressStepNames[2] := CustomMessage('StepPreferences');
  ProgressStepNames[3] := CustomMessage('StepFinishing');
  for Index := 0 to 3 do
  begin
    ProgressStepStates[Index] := issPending;
    ProgressStepLabels[Index] := TNewStaticText.Create(WizardForm);
    ProgressStepLabels[Index].Parent := WizardForm.InstallingPage;
    ProgressStepLabels[Index].AutoSize := False;
    ProgressStepLabels[Index].WordWrap := True;
    ProgressStepLabels[Index].Font.Size := 10;
    UpdateStepLabel(Index);
  end;

  TechnicalDetailsTitle := TNewStaticText.Create(WizardForm);
  TechnicalDetailsTitle.Parent := WizardForm.InstallingPage;
  TechnicalDetailsTitle.AutoSize := False;
  TechnicalDetailsTitle.Caption := CustomMessage('TechnicalDetails');
  TechnicalDetailsTitle.Font.Size := 10;
  TechnicalDetailsTitle.Font.Style := [fsBold];
  TechnicalDetailsTitle.Visible := False;

  TechnicalDetailsMemo := TNewMemo.Create(WizardForm);
  TechnicalDetailsMemo.Parent := WizardForm.InstallingPage;
  TechnicalDetailsMemo.ReadOnly := True;
  TechnicalDetailsMemo.ScrollBars := ssVertical;
  TechnicalDetailsMemo.WordWrap := True;
  TechnicalDetailsMemo.WantReturns := False;
  TechnicalDetailsMemo.Font.Name := 'Consolas';
  TechnicalDetailsMemo.Font.Size := 9;
  TechnicalDetailsMemo.Visible := False;

  TechnicalDetailsButton := TNewButton.Create(WizardForm);
  TechnicalDetailsButton.Parent := WizardForm.InstallingPage;
  TechnicalDetailsButton.Caption := CustomMessage('ShowDetails');
  TechnicalDetailsButton.OnClick := @ToggleTechnicalDetails;

  TechnicalDetailsVisible := False;
  CurrentInstallStage := -1;
  LastLoggedProgressBucket := -1;
  LastProgressPercent := 0;
  LastNativeStatus := '';
  LastNativeFilename := '';
  UpdateProgressSummary;
  AppendTechnicalDetail(CustomMessage('LogStarted'));
end;

procedure InitializeFinishedPage;
begin
  WizardForm.WizardBitmapImage2.Visible := False;

  FinishedBrandImage := TBitmapImage.Create(WizardForm);
  FinishedBrandImage.Parent := WizardForm.FinishedPage;
  FinishedBrandImage.Bitmap.Assign(WizardForm.WizardSmallBitmapImage.Bitmap);
  FinishedBrandImage.Stretch := True;
  FinishedBrandImage.Center := True;

  WizardForm.FinishedHeadingLabel.Caption := CustomMessage('CompletedTitle');
  WizardForm.FinishedHeadingLabel.AutoSize := False;
  WizardForm.FinishedHeadingLabel.Alignment := taCenter;
  WizardForm.FinishedHeadingLabel.Font.Name := 'Segoe UI';
  WizardForm.FinishedHeadingLabel.Font.Size := 22;
  WizardForm.FinishedHeadingLabel.Font.Style := [fsBold];

  WizardForm.FinishedLabel.Caption := CustomMessage('CompletedBody');
  WizardForm.FinishedLabel.AutoSize := False;
  WizardForm.FinishedLabel.Alignment := taCenter;
  WizardForm.FinishedLabel.Font.Size := 11;
  WizardForm.FinishedLabel.WordWrap := True;
  WizardForm.RunList.BorderStyle := bsNone;
  WizardForm.RunList.Flat := True;
  WizardForm.RunList.ShowLines := False;
end;

procedure InitializeWizard;
begin
  WizardForm.PageNameLabel.AutoSize := True;
  WizardForm.PageNameLabel.Font.Name := 'Segoe UI';
  WizardForm.PageNameLabel.Font.Size := 12;
  WizardForm.PageNameLabel.Font.Style := [fsBold];
  WizardForm.PageDescriptionLabel.Font.Size := 10;

  WizardForm.BackButton.Caption := WithoutMnemonic(WizardForm.BackButton.Caption);
  WizardForm.NextButton.Caption := WithoutMnemonic(WizardForm.NextButton.Caption);
  WizardForm.CancelButton.Caption := WithoutMnemonic(WizardForm.CancelButton.Caption);
  WizardForm.DirBrowseButton.Caption := WithoutMnemonic(WizardForm.DirBrowseButton.Caption);
  WizardForm.GroupBrowseButton.Caption := WithoutMnemonic(WizardForm.GroupBrowseButton.Caption);
  WizardForm.NoIconsCheck.Caption := WithoutMnemonic(WizardForm.NoIconsCheck.Caption);
  WizardForm.YesRadio.Caption := WithoutMnemonic(WizardForm.YesRadio.Caption);
  WizardForm.NoRadio.Caption := WithoutMnemonic(WizardForm.NoRadio.Caption);
  WizardForm.PreparingYesRadio.Caption := WithoutMnemonic(WizardForm.PreparingYesRadio.Caption);
  WizardForm.PreparingNoRadio.Caption := WithoutMnemonic(WizardForm.PreparingNoRadio.Caption);

  InitializeWelcomePage;
  InitializeDocumentPages;
  InitializeProgressPage;
  InitializeFinishedPage;

  WizardForm.OnResize := @LayoutInstaller;
  LayoutInstaller(WizardForm);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not WizardSilent then
  begin
    CurrentInstallStage := 0;
    SetStepState(0, issActive);
    ProgressActivityLabel.Caption := CustomMessage('LogPreparing');
    AppendTechnicalDetail(CustomMessage('LogPreparing'));
    AppendTechnicalDetail(FmtMessage(CustomMessage('LogSelectedTasks'), [WizardSelectedTasks(False)]));
  end;
end;

procedure BeginShellConfiguration;
begin
  if WizardSilent or (CurrentInstallStage >= 2) then
    Exit;

  SetStepState(1, issComplete);
  SetStepState(2, issActive);
  CurrentInstallStage := 2;
  ProgressActivityLabel.Caption := CustomMessage('LogPreferences');
  AppendTechnicalDetail(CustomMessage('LogPreferences'));
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
  Bucket: Integer;
  NativeStatus: String;
  NativeFilename: String;
begin
  if WizardSilent or (MaxProgress <= 0) then
    Exit;

  WizardForm.ProgressGauge.Min := 0;
  WizardForm.ProgressGauge.Max := MaxProgress;
  WizardForm.ProgressGauge.Position := CurProgress;
  Percent := (CurProgress * 100) div MaxProgress;
  LastProgressPercent := Percent;
  UpdateProgressSummary;

  NativeStatus := WizardForm.StatusLabel.Caption;
  if (NativeStatus <> '') and (NativeStatus <> LastNativeStatus) then
  begin
    LastNativeStatus := NativeStatus;
    ProgressActivityLabel.Caption := NativeStatus;
    AppendTechnicalDetail(NativeStatus);
  end;

  NativeFilename := WizardForm.FilenameLabel.Caption;
  if (NativeFilename <> '') and (NativeFilename <> LastNativeFilename) then
  begin
    LastNativeFilename := NativeFilename;
    AppendTechnicalDetail(FmtMessage(CustomMessage('LogFile'), [NativeFilename]));
  end;

  Bucket := Percent div 10;
  if (Bucket > LastLoggedProgressBucket) and (Percent > 0) then
  begin
    LastLoggedProgressBucket := Bucket;
    AppendTechnicalDetail(FmtMessage(CustomMessage('LogProgress'), [IntToStr(Percent)]));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if WizardSilent then
    Exit;

  if CurStep = ssInstall then
  begin
    SetStepState(0, issComplete);
    SetStepState(1, issActive);
    CurrentInstallStage := 1;
    ProgressActivityLabel.Caption := CustomMessage('LogInstalling');
    AppendTechnicalDetail(FmtMessage(CustomMessage('LogDestination'), [ExpandConstant('{app}')]));
    AppendTechnicalDetail(CustomMessage('LogInstalling'));
  end
  else if CurStep = ssPostInstall then
  begin
    if CurrentInstallStage < 2 then
      SetStepState(1, issComplete);
    SetStepState(2, issComplete);
    SetStepState(3, issActive);
    CurrentInstallStage := 3;
    ProgressActivityLabel.Caption := CustomMessage('LogFinishing');
    AppendTechnicalDetail(CustomMessage('LogFinishing'));
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    WizardForm.NextButton.Caption := CustomMessage('InstallAction')
  else if CurPageID = wpReady then
    WizardForm.NextButton.Caption := WithoutMnemonic(SetupMessage(msgButtonInstall))
  else if CurPageID = wpFinished then
  begin
    WizardForm.NextButton.Caption := WithoutMnemonic(SetupMessage(msgButtonFinish));
    if not WizardSilent then
    begin
      SetStepState(3, issComplete);
      CurrentInstallStage := 4;
      LastProgressPercent := 100;
      ProgressActivityLabel.Caption := CustomMessage('LogCompleted');
      UpdateProgressSummary;
      AppendTechnicalDetail(CustomMessage('LogCompleted'));
    end;
    LayoutFinishedPage;
  end
  else
    WizardForm.NextButton.Caption := WithoutMnemonic(SetupMessage(msgButtonNext));

  if CurPageID = wpInstalling then
  begin
    WizardForm.PageNameLabel.Caption := CustomMessage('ProgressTitle');
    WizardForm.PageDescriptionLabel.Caption := CustomMessage('ProgressSubtitle');
    LayoutProgressPage;
    ProgressSummaryLabel.Repaint;
    ProgressActivityLabel.Repaint;
  end;
end;

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
