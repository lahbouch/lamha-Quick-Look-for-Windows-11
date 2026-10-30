; Per-user install: no administrator rights, nothing outside the user's profile.
#define AppName "لمحة"
#define AppVersion "1.0.0"

[Setup]
AppId={{7E0C7F58-5A44-4D0F-9E08-3C1C7AB1F9D2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Lamha
DefaultDirName={localappdata}\Programs\Lamha
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Lamha-Setup-{#AppVersion}
SetupIconFile=..\src\Lamha\Assets\lamha.ico
UninstallDisplayIcon={app}\Lamha.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
LanguageDetectionMethod=none

[Languages]
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "startup"; Description: "تشغيل لمحة تلقائيًا مع بدء Windows"
Name: "desktop"; Description: "إنشاء اختصار على سطح المكتب"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Lamha.exe"
Name: "{group}\إلغاء تثبيت {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\Lamha.exe"; Tasks: desktop

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lamha"; ValueData: """{app}\Lamha.exe"" --tray"; Tasks: startup
; The app can also write this value from its settings, so uninstall removes it whatever the task said.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Lamha"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Lamha.exe"; Description: "تشغيل لمحة الآن"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; Settings, logs, the LibreOffice profile, and cached PDF copies of previewed documents.
Type: filesandordirs; Name: "{localappdata}\Lamha"
Type: filesandordirs; Name: "{userappdata}\Lamha"
Type: filesandordirs; Name: "{localappdata}\Temp\Lamha"
Type: filesandordirs; Name: "{app}"

[Code]
procedure StopRunningApp;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM Lamha.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;
