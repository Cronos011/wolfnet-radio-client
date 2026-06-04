#define AppName "WolfNET Radio"
#ifndef AppVersion
#define AppVersion "0.1.8"
#endif
#define AppPublisher "Luna Wolves Legion"
#define AppURL "https://gwrecon.com/comms"
#define AppExeName "WolfNETRadio.exe"

[Setup]
AppId={{B1C2D3E4-F5A6-7890-BCDE-F12345678901}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
DefaultDirName={autopf}\WolfNET Radio
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=WolfNET-Radio-Client-Setup
SetupIconFile=..\WolfNETRadio\Assets\wolfnet-radio.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
