; PicasaPortable - Portable Sandboxed Picasa 3.9 installer
; Built with InnoSetup 6
;
; Distribution: A single .exe that extracts to a user-chosen folder.
; No registry entries, no Start Menu changes, no system modifications.
; The Picasa.exe inside the extracted folder then handles its own first-run setup.

#define MyAppName "Picasa Portable"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "PicasaPortable"
#define MyAppExeName "Picasa.exe"

[Setup]
AppId={{A1B2C3D4-PICA-SAPO-RTAB-LE2026XX1234}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; Portable mode: install to user-chosen folder, default to drive root
DefaultDirName={sd}\PicasaPortable
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; No admin needed for extraction (Picasa.exe will request UAC when first run)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output the single installer .exe here
OutputDir=E:\PicasaPortable\release
OutputBaseFilename=PicasaPortable-Setup-v{#MyAppVersion}
SetupIconFile=E:\PicasaPortable\picasa.ico

; Maximum compression for smaller download
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Don't create an uninstall entry in Add/Remove Programs (truly portable)
Uninstallable=no
CreateUninstallRegKey=no

; Visual
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Main launcher
Source: "E:\PicasaPortable\Picasa.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "E:\PicasaPortable\picasa.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "E:\PicasaPortable\README.md"; DestDir: "{app}"; Flags: ignoreversion

; Sandboxie binaries (pre-extracted)
Source: "E:\PicasaPortable\Sandboxie-Plus\*"; DestDir: "{app}\Sandboxie-Plus"; Flags: ignoreversion recursesubdirs createallsubdirs

; Pre-installed Picasa in sandbox data folder (CLEAN staging copy: no dev DB,
; no indexed thumbnails, no cross-drive references — fresh installs start empty)
Source: "E:\PicasaPortable\dist\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Empty Pictures folder for user's photos
Name: "{app}\Pictures"

[Icons]
; Optional desktop shortcut
Name: "{userdesktop}\Picasa Portable"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\picasa.ico"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
; Offer to launch Picasa immediately after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Picasa Portable now"; Flags: postinstall nowait skipifsilent

[Code]
function GetDataDirSize(): String;
begin
  Result := 'About 250 MB';
end;
