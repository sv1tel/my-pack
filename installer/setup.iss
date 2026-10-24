#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Launcher"
#define AppPublisher "sv1tel"
#define AppExe "PackLauncher.exe"
#define DataFolder "MyPackLauncher"

[Setup]
AppId={{2BECCD6C-D910-4695-9018-B409309496CC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename={#DataFolder}-Setup-{#AppVersion}
SetupIconFile=..\src\PackLauncher\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no
DisableWelcomePage=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.Shortcuts=Ярлыки:
ru.StartMenuIcon=Создать ярлык в меню «Пуск»
ru.DesktopIcon=Создать ярлык на рабочем столе
ru.Launch=Запустить %1
ru.DotNetInstalling=Установка .NET Desktop Runtime…
ru.DotNetHint=Он нужен для работы лаунчера. Это займёт около минуты.
ru.DotNetFailed=Не удалось установить .NET (код %1). Лаунчер попросит установить его при первом запуске.
ru.DeleteData=Удалить также скачанную игру, сборку и настройки лаунчера?

uk.Shortcuts=Ярлики:
uk.StartMenuIcon=Створити ярлик у меню «Пуск»
uk.DesktopIcon=Створити ярлик на робочому столі
uk.Launch=Запустити %1
uk.DotNetInstalling=Встановлення .NET Desktop Runtime…
uk.DotNetHint=Він потрібен для роботи лаунчера. Це займе близько хвилини.
uk.DotNetFailed=Не вдалося встановити .NET (код %1). Лаунчер попросить встановити його під час першого запуску.
uk.DeleteData=Видалити також завантажену гру, збірку та налаштування лаунчера?

en.Shortcuts=Shortcuts:
en.StartMenuIcon=Create a Start menu shortcut
en.DesktopIcon=Create a desktop shortcut
en.Launch=Launch %1
en.DotNetInstalling=Installing .NET Desktop Runtime…
en.DotNetHint=The launcher needs it to run. This takes about a minute.
en.DotNetFailed=.NET could not be installed (code %1). The launcher will ask to install it on first start.
en.DeleteData=Also delete the downloaded game, modpack and launcher settings?

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:Launch,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
Type: files; Name: "{app}\defaults.json"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDotNetDesktopInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and (not IsDotNetDesktopInstalled) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        DownloadPage.SetText(CustomMessage('DotNetInstalling'), CustomMessage('DotNetHint'));
        DownloadPage.ProgressBar.Style := npbstMarquee;
        if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'), '/install /passive /norestart', '',
          SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetFailed'), ['-1']), mbError, MB_OK, IDOK)
        else if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
          SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
      except
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\defaults.json'),
      '{ "language": "' + ActiveLanguage + '" }', False);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\{#DataFolder}'), True, True, True);
end;
