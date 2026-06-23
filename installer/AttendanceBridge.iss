; Inno Setup script for the Shikzya Attendance Bridge agent.
; Produces a double-click ShikzyaAgentSetup.exe: the school runs it, pastes the
; site token (or it's pre-filled), clicks Next, and the service installs + starts.
;
; Build once (free tool: https://jrsoftware.org/isinfo.php):
;   1) run  scripts\publish.ps1   (creates scripts\publish\)
;   2) open this file in Inno Setup and click Build  (or: ISCC AttendanceBridge.iss)
; Pre-key a per-school installer (silent):
;   ShikzyaAgentSetup.exe /VERYSILENT /token=<siteToken> /apiurl=https://app.shikzya.com

#define AppName "Shikzya Attendance Bridge"
#define AppVersion "1.0.0"
#ifndef SourceDir
  #define SourceDir "..\scripts\publish"
#endif

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\ShikzyaAttendanceBridge
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=
OutputDir=Output
OutputBaseFilename=ShikzyaAgentSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Code]
var
  CfgPage: TInputQueryWizardPage;

function GetParam(Name, Default: String): String;
var V: String;
begin
  V := ExpandConstant('{param:' + Name + '|}');
  if V = '' then Result := Default else Result := V;
end;

procedure InitializeWizard;
begin
  CfgPage := CreateInputQueryPage(wpSelectDir,
    'Shikzya settings', 'Connect this agent to Shikzya',
    'Enter the values from Shikzya admin (Sites & Devices > the site''s install token).');
  CfgPage.Add('API base URL:', False);
  CfgPage.Add('Site token:', False);
  CfgPage.Values[0] := GetParam('apiurl', 'https://app.shikzya.com');
  CfgPage.Values[1] := GetParam('token', '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = CfgPage.ID) and (Trim(CfgPage.Values[1]) = '') then
  begin
    MsgBox('Please paste the Site token from Shikzya.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Exe, Params: String; Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Exe := ExpandConstant('{app}\AttendanceBridge.exe');
    Params := 'install --token "' + CfgPage.Values[1] + '" --api "' + CfgPage.Values[0] + '"';
    if not Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('The service could not be installed (code ' + IntToStr(Code) + ').' + #13#10 +
             'Check the logs in the install folder.', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{app}\AttendanceBridge.exe'), 'uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
