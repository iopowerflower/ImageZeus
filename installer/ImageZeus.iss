; ImageZeus Inno Setup Installer Script
; Requires Inno Setup 6.x  -  https://jrsoftware.org/isinfo.php
;
; Before compiling this script:
;   1. Run publish.bat from the project root
;   2. Open this .iss in Inno Setup Compiler and click Build

#define MyAppName "ImageZeus"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ImageZeus"
#define MyAppExeName "ImageZeus.exe"
#define PublishDir "..\publish"
#define IconFile "..\zeusicon.ico"

[Setup]
AppId={{B7E3F2A1-9C4D-4F8B-A6E2-1D3C5B7A9E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\installer_output
OutputBaseFilename=ImageZeusSetup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Associate with image files (jpg, png, bmp, gif, webp, tif)"; GroupDescription: "File Associations:"; Flags: checkedonce
Name: "startwithwindows"; Description: "Start at login (for fast image opening)"; GroupDescription: "Background Service:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#IconFile}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\zeusicon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\zeusicon.ico"; Tasks: desktopicon

[Registry]
; ProgID
Root: HKLM; Subkey: "SOFTWARE\Classes\ImageZeus.Image"; ValueType: string; ValueData: "Image File - ImageZeus"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Classes\ImageZeus.Image\DefaultIcon"; ValueType: string; ValueData: "{app}\zeusicon.ico,0"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Classes\ImageZeus.Image\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

; Registered Application capabilities (appears in Windows Default Apps)
Root: HKLM; Subkey: "SOFTWARE\ImageZeus"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "ImageZeus"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Fast, lightweight image viewer"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gif"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tif"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKLM; Subkey: "SOFTWARE\ImageZeus\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tiff"; ValueData: "ImageZeus.Image"; Flags: uninsdeletekey; Tasks: fileassoc

; Master registered-applications list
Root: HKLM; Subkey: "SOFTWARE\RegisteredApplications"; ValueType: string; ValueName: "ImageZeus"; ValueData: "SOFTWARE\ImageZeus\Capabilities"; Flags: uninsdeletevalue

; Start at login (daemon mode)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ImageZeus"; ValueData: """{app}\{#MyAppExeName}"" --daemon"; Flags: uninsdeletevalue; Tasks: startwithwindows

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--daemon"; Description: "Start ImageZeus background service"; Flags: nowait postinstall skipifsilent
