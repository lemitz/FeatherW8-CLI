#define AppName "FeatherW8"
#define AppVersion "0.2.0"
#define AppExeName "FeatherW8.exe"
#define ContextVerb "FeatherW8.Play"

[Setup]
AppId={{05055166-f07c-424c-b205-a5af517a91d1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=FeatherW8 Project
DefaultDirName={localappdata}\Programs\FeatherW8
DefaultGroupName=FeatherW8
SetupIconFile=..\icons\FeatherW8.ico
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
OutputDir=..\dist
OutputBaseFilename=FeatherW8-Setup-win-x64
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.ContextTask=Add Explorer context-menu entries for audio files and folders
russian.ContextTask=Добавить пункты контекстного меню для аудиофайлов и папок
english.HelpShortcut=FeatherW8 — help
russian.HelpShortcut=FeatherW8 — справка
english.PlayVerb=Play in FeatherW8
russian.PlayVerb=Проиграть в FeatherW8
english.FolderVerb=Open folder in FeatherW8
russian.FolderVerb=Открыть папку в FeatherW8

[Tasks]
Name: "filecontext"; Description: "{cm:ContextTask}"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Excludes: "FeatherW8.properties"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\win-x64\FeatherW8.properties"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{cm:HelpShortcut}"; Filename: "{app}\{#AppExeName}"; Parameters: "--help"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp3\shell\{#ContextVerb}"; ValueType: string; ValueData: "{cm:PlayVerb}"; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp3\shell\{#ContextVerb}\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wav\shell\{#ContextVerb}"; ValueType: string; ValueData: "{cm:PlayVerb}"; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wav\shell\{#ContextVerb}\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogg\shell\{#ContextVerb}"; ValueType: string; ValueData: "{cm:PlayVerb}"; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogg\shell\{#ContextVerb}\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.opus\shell\{#ContextVerb}"; ValueType: string; ValueData: "{cm:PlayVerb}"; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.opus\shell\{#ContextVerb}\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\Directory\shell\{#ContextVerb}"; ValueType: string; ValueData: "{cm:FolderVerb}"; Flags: uninsdeletekey; Tasks: filecontext
Root: HKCU; Subkey: "Software\Classes\Directory\shell\{#ContextVerb}\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey; Tasks: filecontext
