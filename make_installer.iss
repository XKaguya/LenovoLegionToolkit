#include "InnoDependencies\Scripts\install_dotnet.iss"

#define MyAppName "Lenovo Legion Toolkit"
#define MyAppNameCompact "LenovoLegionToolkit"
#define MyAppPublisher "LenovoLegionToolkit-Team"
#define MyAppURL "https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit"
#define MyAppExeName "Lenovo Legion Toolkit.exe"
#define MyAppGitHub "https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit"
#define MyAppLegionDiscord "https://discord.com/invite/legionseries"
#define MyAppLOQDiscord "https://discord.gg/3GKzQtwdNf"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.1"
#endif

#ifndef MyBuildDate
  #define MyBuildDate "00000000"
#endif

[Setup]
UsedUserAreasWarning=false
AppId={{0C37B9AC-9C3D-4302-8ABB-125C7C7D83D5}
AppMutex=LenovoLegionToolkit_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={userpf}\{#MyAppNameCompact}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
PrivilegesRequired=admin
OutputBaseFilename=LenovoLegionToolkitSetup-v{#MyAppVersion}_Build{#MyBuildDate}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=build_installer
ArchitecturesInstallIn64BitMode=x64compatible
WizardSmallImageFile=InnoDependencies\Images\logo.png
SetupIconFile=InnoDependencies\Images\setup_icon.ico
SetupLogging=yes

[Code]
function InitializeSetup: Boolean;
begin
  InstallDotNetDesktopRuntime;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Unregister Package
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name ''eef45acd-2cf3-4d7d-9d33-92f37c74cc31'' | Remove-AppxPackage"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Remove Certificate from LocalMachine
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Remove Certificate from CurrentUser (just in case)
    Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\CurrentUser\TrustedPeople | Where-Object { $_.Subject -match ''LenovoLegionToolkit'' } | Remove-Item -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure OpenBrowser(Url: string);
var
  ResultCode: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure GitHubLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppGitHub}');
end;

procedure LegionDiscordLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppLegionDiscord}');
end;

procedure LOQDiscordLinkClick(Sender: TObject);
begin
  OpenBrowser('{#MyAppLOQDiscord}');
end;

procedure CurPageChanged(CurPageID: Integer);
var
  GitHubLink, LegionDiscordLink, LOQDiscordLink: TNewStaticText;
  Offset: Integer;
begin
  if CurPageID = wpFinished then
  begin
    Offset := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(4);

    GitHubLink := TNewStaticText.Create(WizardForm);
    GitHubLink.Parent := WizardForm.FinishedPage;
    GitHubLink.Top := Offset;
    GitHubLink.Left := WizardForm.FinishedLabel.Left;
    GitHubLink.Caption := ExpandConstant('{cm:VisitGitHub}');
    GitHubLink.Font.Color := clBlue;
    GitHubLink.Font.Style := [fsUnderline];
    GitHubLink.Cursor := crHand;
    GitHubLink.OnClick := @GitHubLinkClick;

    LegionDiscordLink := TNewStaticText.Create(WizardForm);
    LegionDiscordLink.Parent := WizardForm.FinishedPage;
    LegionDiscordLink.Top := GitHubLink.Top + GitHubLink.Height + ScaleY(8);
    LegionDiscordLink.Left := WizardForm.FinishedLabel.Left;
    LegionDiscordLink.Caption := ExpandConstant('{cm:JoinLegionDiscord}');
    LegionDiscordLink.Font.Color := clBlue;
    LegionDiscordLink.Font.Style := [fsUnderline];
    LegionDiscordLink.Cursor := crHand;
    LegionDiscordLink.OnClick := @LegionDiscordLinkClick;

    LOQDiscordLink := TNewStaticText.Create(WizardForm);
    LOQDiscordLink.Parent := WizardForm.FinishedPage;
    LOQDiscordLink.Top := LegionDiscordLink.Top + LegionDiscordLink.Height + ScaleY(8);
    LOQDiscordLink.Left := WizardForm.FinishedLabel.Left;
    LOQDiscordLink.Caption := ExpandConstant('{cm:JoinLOQDiscord}');
    LOQDiscordLink.Font.Color := clBlue;
    LOQDiscordLink.Font.Style := [fsUnderline];
    LOQDiscordLink.Cursor := crHand;
    LOQDiscordLink.OnClick := @LOQDiscordLinkClick;

    if WizardForm.RunList.Visible then
      WizardForm.RunList.Top := LOQDiscordLink.Top + LOQDiscordLink.Height + ScaleY(12);
  end;
end;
[CustomMessages]
VisitGitHub=Visit GitHub Repository
JoinLegionDiscord=Join Legion Series Discord Community
JoinLOQDiscord=Join LOQ Series Discord Community

[Languages]
Name: "en";      MessagesFile: "compiler:Default.isl"
Name: "ar";      MessagesFile: "InnoDependencies\Languages\Arabic.isl"
Name: "bs";      MessagesFile: "InnoDependencies\Languages\Bosnian.isl"
Name: "bg";      MessagesFile: "InnoDependencies\Languages\Bulgarian.isl"
Name: "zhhans";  MessagesFile: "InnoDependencies\Languages\ChineseSimplified.isl"
Name: "zhhant";  MessagesFile: "InnoDependencies\Languages\ChineseTraditional.isl"
Name: "cs";      MessagesFile: "InnoDependencies\Languages\Czech.isl"
Name: "nlnl";    MessagesFile: "InnoDependencies\Languages\Dutch.isl"
Name: "fr";      MessagesFile: "InnoDependencies\Languages\French.isl"
Name: "de";      MessagesFile: "InnoDependencies\Languages\German.isl"
Name: "el";      MessagesFile: "InnoDependencies\Languages\Greek.isl"
Name: "hu";      MessagesFile: "InnoDependencies\Languages\Hungarian.isl"
Name: "it";      MessagesFile: "InnoDependencies\Languages\Italian.isl"
Name: "ja";      MessagesFile: "InnoDependencies\Languages\Japanese.isl"
Name: "ko";      MessagesFile: "InnoDependencies\Languages\Korean.isl"
Name: "lv";      MessagesFile: "InnoDependencies\Languages\Latvian.isl"
Name: "no";      MessagesFile: "InnoDependencies\Languages\Norwegian.isl"
Name: "pl";      MessagesFile: "InnoDependencies\Languages\Polish.isl"
Name: "pt";      MessagesFile: "InnoDependencies\Languages\Portuguese.isl"
Name: "ptbr";    MessagesFile: "InnoDependencies\Languages\BrazilianPortuguese.isl"
Name: "ro";      MessagesFile: "InnoDependencies\Languages\Romanian.isl"
Name: "ru";      MessagesFile: "InnoDependencies\Languages\Russian.isl"
Name: "sk";      MessagesFile: "InnoDependencies\Languages\Slovak.isl"
Name: "es";      MessagesFile: "InnoDependencies\Languages\Spanish.isl"
Name: "tr";      MessagesFile: "InnoDependencies\Languages\Turkish.isl"
Name: "ukr";     MessagesFile: "InnoDependencies\Languages\Ukrainian.isl"
Name: "uz";      MessagesFile: "InnoDependencies\Languages\Uzbek.isl"
Name: "vi";      MessagesFile: "InnoDependencies\Languages\Vietnamese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Unregister existing package identity (required for upgrades)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-AppxPackage -Name 'eef45acd-2cf3-4d7d-9d33-92f37c74cc31' | Remove-AppxPackage -ErrorAction SilentlyContinue"""; Flags: runhidden; StatusMsg: "Removing previous identity..."
; Trust the self-signed certificate (required for registration)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Import-Certificate -FilePath '{app}\LenovoLegionToolkit.LampArray.cer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"""; Flags: runhidden; StatusMsg: "Trusting application identity..."
; Register the Sparse Package identity (conditional: use .msix if present, else raw manifest)
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""if (Test-Path '{app}\LenovoLegionToolkit.LampArray.msix') {{ Add-AppxPackage -Path '{app}\LenovoLegionToolkit.LampArray.msix' -ExternalLocation '{app}' } else {{ Add-AppxPackage -Register '{app}\AppxManifest.xml' -ExternalLocation '{app}' }"""; Flags: runhidden; StatusMsg: "Registering application identity..."

Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: runascurrentuser nowait postinstall

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\{#MyAppNameCompact}"

[UninstallRun]
RunOnceId: "DelAutorun"; Filename: "schtasks"; Parameters: "/Delete /TN ""LenovoLegionToolkit_Autorun_6efcc882-924c-4cbc-8fec-f45c25696f98"" /F"; Flags: runhidden
