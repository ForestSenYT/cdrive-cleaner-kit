; Installer for C 盘清理, compiled by Inno Setup 6. Build it with build\release.ps1, which passes:
;   /DAppVersion=<version>   from Directory.Build.props
;   /DSourceDir=<folder>     the self-contained publish output
;   /DSign and /Scdsign=...  when signing, so the installer and its uninstaller are signed as well

#ifndef AppVersion
  #error AppVersion is missing: build with build\release.ps1
#endif
#ifndef SourceDir
  #error SourceDir is missing: build with build\release.ps1
#endif

#define AppName "C 盘清理"
#define AppExe "CDriveCleaner.exe"
#define RepoUrl "https://github.com/ForestSenYT/cdrive-cleaner-kit"

[Setup]
; Identifies the program across versions, so a newer installer upgrades in place. Never change it.
AppId={{4D0B061D-0E69-44D9-8BD9-4A6B703CF2DF}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ForestSenYT
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} 安装程序
; Installs for the current user without administrator rights; the first page also offers installing for everyone.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\CDriveCleaner
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\src\CDriveCleaner.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputBaseFilename=CDriveCleaner-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
#ifdef Sign
SignTool=cdsign
SignedUninstaller=yes
#endif

[Languages]
; From the Inno Setup repository (Files\Languages); version 6.7.3 does not install it yet.
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The clean history and error log the program keeps for the user.
Type: filesandordirs; Name: "{localappdata}\CDriveCleaner"
