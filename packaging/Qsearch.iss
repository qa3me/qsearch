#define MyAppName "Qsearch"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\work\package\" + MyAppVersion + "\stage"
#endif

[Setup]
AppId={{5C455243-4800-5133-8000-515345415243}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Behzad Qasemi
DefaultDirName={autopf}\Qsearch
DefaultGroupName=Qsearch
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
OutputBaseFilename=Qsearch-{#MyAppVersion}-win-x64-setup-unsigned
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\Qsearch.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Qsearch"; Filename: "{app}\Qsearch.exe"
Name: "{autodesktop}\Qsearch"; Filename: "{app}\Qsearch.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\Qsearch.exe"; Description: "Launch Qsearch"; Flags: nowait postinstall skipifsilent
