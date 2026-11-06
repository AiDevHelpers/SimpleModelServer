; SimpleModelServer installer (Inno Setup 6). Replaces the WiX/MSI package in src\Installers\wix.
;
; Built by installer\build-installer.ps1, which publishes the payloads and passes them in as
; preprocessor defines. Compile by hand with:
;   ISCC.exe /DMyAppVersion=1.0.42 /DMyAppNumericVersion=1.0.42.0 /DServerDir=... SimpleModelServer.iss
;
; What it does, matching the WiX package it replaces:
;   - installs the router/service payload to {autopf}\SimpleModelServer (required)
;   - optional: chat app, Visual Studio extension, VS Code extension (Components page, Custom setup)
;   - registers the "SimpleModelServer" Windows service (LocalSystem, auto start, same restart
;     schedule as the WiX package), stops it before files are replaced and starts it afterwards
;   - registers the netsh URL ACL for the router's HttpListener prefix, read from the config
;     when one exists, else localhost:11435
;   - warns (never blocks) if ollama is not on PATH, since the router cannot become healthy without it
;
; Deliberately NOT done: writing SimpleModelServer.config.json. The server writes the full stock
; config (models included) itself on first run. A partial config written here would be left alone
; by that step and produce a config with no models, which is the failure that motivated the
; config-schema fix.
;
; Uninstall removes the service and URL ACL but keeps %ProgramData%\SimpleModelServer (config and
; the Ollama model store), so an upgrade or reinstall does not lose either.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "0.0.0.0"
#endif
#ifndef ServerDir
  #define ServerDir "..\build\staging\installer\server"
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif
#ifndef OutputDir
  #define OutputDir "..\build\dist\installer"
#endif

#define MyAppName "Simple Model Server"
#define MyAppPublisher "Richard Sperry"
#define MyServiceName "SimpleModelServer"
#define MyServerExe "SimpleModelServer.Server.exe"

[Setup]
; AppId is this installer's identity for upgrades. It must never change. It is NOT the WiX
; UpgradeCode (7ADD086D-...), so an installed MSI is detected by the migration check in
; InitializeSetup instead of being upgraded in place.
AppId={{1D874A35-B2CB-4702-8C54-A88AB6F34AB5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductVersion={#MyAppNumericVersion}
DefaultDirName={autopf}\SimpleModelServer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName} {#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=SimpleModelServer-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The service must be stopped and restarted by this installer; Restart Manager would close it
; without restarting it.
CloseApplications=no
RestartIfNeededByRun=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Service and router (recommended)"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "server"; Description: "Router service (required)"; Types: full custom; Flags: fixed
#ifdef HasChat
Name: "chat"; Description: "Chat app (desktop client)"; Types: custom
#endif
#ifdef HasVsExtension
Name: "vsext"; Description: "Visual Studio extension (.vsix, installed manually)"; Types: custom
#endif
#ifdef HasVsCodeExtension
Name: "vscode"; Description: "VS Code extension (.vsix, installed manually)"; Types: custom
#endif

[Dirs]
; The ProgramData folder holds the config and the Ollama model store. It is never removed.
Name: "{commonappdata}\SimpleModelServer"; Flags: uninsneveruninstall

[Files]
Source: "{#ServerDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Components: server; \
  Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\src\Server\SimpleModelServer.Server\AI_INSTRUCTIONS_REMOTE_ROUTER.md"; \
  DestDir: "{commonappdata}\SimpleModelServer"; Components: server; Flags: ignoreversion
#ifdef HasChat
Source: "{#ChatDir}\*"; DestDir: "{app}\Chat"; Components: chat; Excludes: "*.pdb"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifdef HasVsExtension
Source: "{#VsExtensionVsix}"; DestDir: "{app}\Extensions"; DestName: "SimpleModelServer.Extensions.VS.vsix"; \
  Components: vsext; Flags: ignoreversion
#endif
#ifdef HasVsCodeExtension
Source: "{#VsCodeExtensionVsix}"; DestDir: "{app}\Extensions"; DestName: "SimpleModelServer.Extensions.VSCode.vsix"; \
  Components: vscode; Flags: ignoreversion
#endif

[UninstallDelete]
Type: files; Name: "{commonappdata}\SimpleModelServer\AI_INSTRUCTIONS_REMOTE_ROUTER.md"

[Code]
const
  ServiceName = '{#MyServiceName}';
  UrlAclDefaultHost = 'localhost';
  UrlAclDefaultPort = '11435';
  ServiceDescription = 'Manages the local Ollama instance and exposes a draft-pair router for AI coding tools.';
  MsiUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';

function RunQuiet(const Program_, Params: String): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(Program_, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

function Sc(const Params: String): Integer;
begin
  Result := RunQuiet(ExpandConstant('{sys}\sc.exe'), Params);
end;

function NetExe(const Params: String): Integer;
begin
  Result := RunQuiet(ExpandConstant('{sys}\net.exe'), Params);
end;

function NetshExe(const Params: String): Integer;
begin
  Result := RunQuiet(ExpandConstant('{sys}\netsh.exe'), Params);
end;

function ServiceExists(): Boolean;
begin
  Result := Sc('query ' + ServiceName) = 0;
end;

function ServiceIsRunning(): Boolean;
begin
  { A pipe only works through cmd.exe; passing it to sc.exe directly would not filter anything. }
  Result := RunQuiet(ExpandConstant('{sys}\cmd.exe'),
                     '/c sc query ' + ServiceName + ' | find "RUNNING" >nul') = 0;
end;

{ Reads a top-level-ish JSON value by key from the config text. Handles the quoted-string and
  bare-number forms the router writes. Good enough for routerHost/routerPort in the config the
  router itself produces; it is not a general JSON parser. }
function JsonValueOf(const Text, Key: String; var Value: String): Boolean;
var
  P: Integer;
  S: String;
begin
  Result := False;
  Value := '';
  P := Pos('"' + Key + '"', Text);
  if P = 0 then
    exit;
  S := Copy(Text, P + Length(Key) + 2, Length(Text));
  P := Pos(':', S);
  if P = 0 then
    exit;
  S := Trim(Copy(S, P + 1, Length(S)));
  if (Length(S) > 0) and (S[1] = '"') then
  begin
    Delete(S, 1, 1);
    P := Pos('"', S);
    if P = 0 then
      exit;
    Value := Copy(S, 1, P - 1);
  end
  else
  begin
    P := 1;
    while (P <= Length(S)) and (S[P] >= '0') and (S[P] <= '9') do
      P := P + 1;
    Value := Copy(S, 1, P - 1);
  end;
  Result := Value <> '';
end;

{ The URL the router will listen on, from the config when it already exists (upgrade or reinstall),
  otherwise the stock default. The server writes the stock config on first start, after this
  runs, so a fresh install correctly falls through to the default. }
function RouterUrl(): String;
var
  ConfigPath, Host, Port: String;
  Raw: AnsiString;
begin
  Host := UrlAclDefaultHost;
  Port := UrlAclDefaultPort;
  ConfigPath := ExpandConstant('{commonappdata}\SimpleModelServer\SimpleModelServer.config.json');
  if FileExists(ConfigPath) and LoadStringFromFile(ConfigPath, Raw) then
  begin
    JsonValueOf(String(Raw), 'routerHost', Host);
    JsonValueOf(String(Raw), 'routerPort', Port);
  end;
  Result := 'http://' + Host + ':' + Port + '/';
end;

procedure EnsureUrlAcl(const Url: String);
begin
  { `netsh http show urlacl` lists a reservation; grep for it so re-running is a no-op. }
  if RunQuiet(ExpandConstant('{sys}\cmd.exe'),
              '/c netsh http show urlacl url=' + Url + ' | find "Reserved URL" >nul') <> 0 then
    NetshExe('http add urlacl url=' + Url + ' user=Everyone');
end;

procedure StopServiceIfRunning();
begin
  if ServiceExists() and ServiceIsRunning() then
    { `net stop` waits until the service has actually stopped, so files are not replaced under a
      running process. }
    NetExe('stop ' + ServiceName + ' /y');
end;

procedure ConfigureService();
var
  ExePath, BinPath: String;
begin
  ExePath := ExpandConstant('{app}\{#MyServerExe}');
  { binPath must be one quoted argument for sc.exe, with the exe path quoted inside it. }
  BinPath := '\"' + ExePath + '\" --service';
  if ServiceExists() then
    Sc('config ' + ServiceName + ' binPath= "' + BinPath + '" start= auto')
  else
    Sc('create ' + ServiceName + ' binPath= "' + BinPath + '" start= auto DisplayName= "{#MyAppName}"');
  Sc('description ' + ServiceName + ' "' + ServiceDescription + '"');
  { Same schedule as the WiX package: restart after 5s, 10s, then 30s; failure counter resets daily. }
  Sc('failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/10000/restart/30000');
end;

function MsiInstallPresent(): Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  KeyPath, DisplayName, UninstallString: String;
begin
  Result := False;
  if not RegGetSubkeyNames(HKLM64, MsiUninstallKey, Keys) then
    exit;
  for I := 0 to GetArrayLength(Keys) - 1 do
  begin
    KeyPath := MsiUninstallKey + '\' + Keys[I];
    if RegQueryStringValue(HKLM64, KeyPath, 'DisplayName', DisplayName) and
       (DisplayName = '{#MyAppName}') and
       RegQueryStringValue(HKLM64, KeyPath, 'UninstallString', UninstallString) and
       (Pos('msiexec', LowerCase(UninstallString)) > 0) then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function RebootPending(): Boolean;
begin
  Result :=
    RegValueExists(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager', 'PendingFileRenameOperations') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') or
    RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if RebootPending() then
  begin
    MsgBox('A Windows restart is pending from a previous update or install. ' +
           'Restart the computer, then run this setup again.', mbCriticalError, MB_OK);
    Result := False;
  end
  else if MsiInstallPresent() then
  begin
    MsgBox('A previous Simple Model Server installation from the MSI package is present. ' +
           'Uninstall it from Apps & Features first, then run this setup again. ' +
           'Your configuration and models in %ProgramData%\SimpleModelServer are kept.',
           mbCriticalError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopServiceIfRunning();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ConfigureService();
    EnsureUrlAcl(RouterUrl());
    { Start if it was running before the upgrade, or on a fresh install. }
    NetExe('start ' + ServiceName);
    if RunQuiet(ExpandConstant('{sys}\cmd.exe'), '/c where ollama >nul 2>&1') <> 0 then
      MsgBox('Simple Model Server was installed, but ollama was not found on PATH. ' +
             'The router will not become healthy until Ollama is installed (https://ollama.com/download). ' +
             'Restart the SimpleModelServer service after installing it.',
             mbInformation, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if ServiceExists() then
    begin
      NetExe('stop ' + ServiceName + ' /y');
      Sc('delete ' + ServiceName);
    end;
    NetshExe('http delete urlacl url=' + RouterUrl());
  end;
  if CurUninstallStep = usPostUninstall then
    MsgBox('Your configuration and models were kept in ' +
           ExpandConstant('{commonappdata}\SimpleModelServer') +
           '. Delete that folder to remove them.', mbInformation, MB_OK);
end;
