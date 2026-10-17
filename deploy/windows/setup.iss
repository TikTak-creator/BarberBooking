#ifndef ReleaseRoot
  #error ReleaseRoot must point to the verified Windows publish folder
#endif
#ifndef OutputRoot
  #define OutputRoot "..\..\release"
#endif
[Setup]
AppId={{965F904D-657B-4332-A846-02CBE6F336BE}
AppName=Bladez Host
AppVersion=1.0.2
AppPublisher=TikTak-creator
AppPublisherURL=https://github.com/TikTak-creator/BarberBooking
AppSupportURL=https://github.com/TikTak-creator/BarberBooking/issues
DefaultDirName={localappdata}\Programs\BladezHost
DefaultGroupName=Bladez
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputRoot}
OutputBaseFilename=Bladez-Setup-1.0.2
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Bladez.Host.exe
SetupLogging=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut / Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "{#ReleaseRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Bladez Host"; Filename: "{app}\Bladez.Host.exe"
Name: "{autodesktop}\Bladez Host"; Filename: "{app}\Bladez.Host.exe"; Tasks: desktopicon
Name: "{group}\Help - Руководство"; Filename: "{app}\START-HERE.html"
[Run]
Filename: "{app}\Bladez.Host.exe"; Description: "Open Bladez Host / Открыть Bladez Host"; Flags: nowait postinstall skipifsilent
; Host data is outside {app}; uninstall never removes a database or encryption key.
