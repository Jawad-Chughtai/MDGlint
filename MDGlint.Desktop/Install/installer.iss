#define MyAppName "MDGlint"
#define MyAppExeName "MDGlint.Desktop.exe"

; The version comes from Directory.Build.props; build-installer.ps1 passes it as /DMyAppVersion=x.y.z.
#ifndef MyAppVersion
  #error MyAppVersion is not set. Build the installer with build-installer.ps1.
#endif

[Setup]
; Never change the AppId: setup finds the installed copy by it, and an update with a different
; one would install a second MDGlint beside the first (docs/decisions.md, "Updates keep working").
AppId={{8650A31D-C8E8-48EC-9A9E-F38B9825ED9E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; The app is x64 only: setup refuses other Windows versions and installs in 64-bit mode
; (Program Files, the 64-bit registry view).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; .NET 10 and the WebView2 Runtime need Windows 10 version 1607 or later. Setup refuses older
; Windows instead of installing an app that can't start (docs/decisions.md, "Windows 10 or later").
MinVersion=10.0.14393
; Installs for the current user by default (no administrator prompt, in the user's Programs
; folder); setup offers installing for all users instead (Program Files, needs administrator).
; {autopf} and the HKA registry root follow that choice, so the registry entries always land
; with the install: per user under HKCU, for all users under HKLM.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ChangesAssociations=yes
SetupIconFile=..\MDGlint.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputBaseFilename=MDGlintSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Tasks]
; The task keeps its original name so upgrades remember the choice; it covers both extensions.
Name: "associate_md"; Description: "Associate .md and .markdown files with MDGlint"; GroupDescription: "File associations:"; Flags: unchecked
; Off unless ticked. The app reads this choice from the registry, and its menu can change it.
Name: "check_updates"; Description: "Check for updates once a day (MDGlint asks before installing one)"; GroupDescription: "Updates:"; Flags: unchecked

[InstallDelete]
; An update installs over the old version. Clear out the kinds of files setup itself puts in
; place first, so none that an older version left behind (and the new one no longer has) stays.
Type: filesandordirs; Name: "{app}\Assets"
Type: filesandordirs; Name: "{app}\runtimes"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\*.xml"

[Files]
; Debug symbols and API docs aren't needed to run the app (and a local build's symbols carry its
; build paths).
Source: "..\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; MDGlint starts its update with /UPDATE=1 and closes; this opens it again once setup is done,
; as the user who started it (not as administrator after an all-users update).
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: IsAppUpdate
Filename: "{app}\{#MyAppExeName}"; Description: "Open MDGlint"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey
; Lists MDGlint under "Open with" for both extensions, whether or not it is the default.
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Flags: uninsdeletekey
; The MDGlint.md file type, always registered and offered for both extensions (OpenWithProgids),
; so Windows names MDGlint with its icon when it asks which app to use.
Root: HKA; Subkey: "Software\Classes\MDGlint.md"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MDGlint.md\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\MDGlint.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MDGlint.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MDGlint.md"; ValueData: ""; Flags: uninsdeletevalue
; With the task ticked, MDGlint.md becomes both extensions' registered handler. Windows 10 and 11
; may still ask the user once to confirm it as the default.
Root: HKA; Subkey: "Software\Classes\.md"; ValueType: string; ValueName: ""; ValueData: "MDGlint.md"; Flags: uninsdeletevalue; Tasks: associate_md
Root: HKA; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueName: ""; ValueData: "MDGlint.md"; Flags: uninsdeletevalue; Tasks: associate_md
; The update-check choice made in setup, always written (1 or 0). The app's own setting, once the
; user changes it there, takes precedence.
Root: HKA; Subkey: "Software\{#MyAppName}"; ValueType: dword; ValueName: "CheckForUpdates"; ValueData: "1"; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: check_updates
Root: HKA; Subkey: "Software\{#MyAppName}"; ValueType: dword; ValueName: "CheckForUpdates"; ValueData: "0"; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: not check_updates

[Code]
const
  // The WebView2 Runtime's EdgeUpdate client key, from Microsoft's distribution guide.
  WebView2ClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2DownloadPage = 'https://developer.microsoft.com/microsoft-edge/webview2/';

// True when MDGlint started this setup to install an update (see [Run]).
function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function HasWebView2Version(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, WebView2ClientKey, 'pv', Version)
    and (Version <> '') and (Version <> '0.0.0.0');
end;

// Installed per machine (the 32-bit registry view) or per user, as Microsoft documents it.
function IsWebView2Installed: Boolean;
begin
  Result := HasWebView2Version(HKLM32) or HasWebView2Version(HKCU);
end;

// Uninstalling asks whether to keep the user's settings, as other apps do; No also removes
// MDGlint's cache and other data. A silent uninstall keeps them. Only the folders of the user
// running the uninstaller are removed: other users' profiles aren't touched.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Do you want to keep your MDGlint settings (theme, remote content and update '
      + 'choices)?' + #13#10#13#10
      + 'Choose No to remove them, along with MDGlint''s cache and other data.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDNO then
    begin
      DelTree(ExpandConstant('{userappdata}\{#MyAppName}'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\{#MyAppName}'), True, True, True);
    end;
end;

// Setup doesn't install the runtime (it ships with Windows 11 and nearly every Windows 10 PC);
// when it is missing, setup says so once MDGlint is installed, and MDGlint links to it too.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not IsWebView2Installed then
    SuppressibleMsgBox('MDGlint needs the Microsoft Edge WebView2 Runtime to show files, and it '
      + 'isn''t installed on this PC. It''s free from Microsoft:' + #13#10#13#10
      + WebView2DownloadPage + #13#10#13#10
      + 'MDGlint is installed and shows this link when you open it.', mbInformation, MB_OK, IDOK);
end;
