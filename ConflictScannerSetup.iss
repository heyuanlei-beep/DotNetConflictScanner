; ============================================================
; DotNetConflictScanner 安装包配置脚本
; 工具: Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
; 使用前请确保已 Release 发布（dotnet publish -c Release）
; ============================================================

[Setup]
AppName=DotNetConflictScanner
AppVersion=1.2
AppPublisher=HYL
AppComments=.NET 依赖版本冲突扫描器，集成 Windows 右键菜单
DefaultDirName={autopf}\DotNetConflictScanner
DefaultGroupName=DotNetConflictScanner
Compression=lzma
SolidCompression=yes
; 生成的安装包输出路径
OutputDir=D:\HYL\DotNetConflictScanner\installer_output
OutputBaseFilename=DotNetConflictScanner_Setup
; 安装时不需要管理员权限（写 HKCU 注册表）
; 如果需要写 HKCR 需要管理员，改为 admin
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName=DotNetConflictScanner (.NET 冲突扫描器)

[Files]
; 单文件发布产物（dotnet publish -c Release -r win-x64 --self-contained false）
Source: "D:\HYL\DotNetConflictScanner\bin\Release\net10.0\win-x64\publish\DotNetConflictScanner.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; --- 注册到"在文件夹空白处右键"菜单 (Directory\Background) ---
Root: HKCR; Subkey: "Directory\Background\shell\DotNetConflictScanner"; ValueType: string; ValueName: ""; ValueData: "扫描 .NET 版本冲突"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\DotNetConflictScanner\command"; ValueType: string; ValueName: ""; ValueData: """{app}\DotNetConflictScanner.exe"" ""%V"""

; --- 注册到"在文件夹图标上右键"菜单 (Directory\shell) ---
Root: HKCR; Subkey: "Directory\shell\DotNetConflictScanner"; ValueType: string; ValueName: ""; ValueData: "扫描 .NET 版本冲突"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\DotNetConflictScanner\command"; ValueType: string; ValueName: ""; ValueData: """{app}\DotNetConflictScanner.exe"" ""%1"""

[Icons]
Name: "{group}\卸载 DotNetConflictScanner"; Filename: "{uninstallexe}"

[Code]
// 安装完成后弹出提示
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    MsgBox('安装完成！' + #13#10 +
           '现在可以在任意文件夹上右键，选择"扫描 .NET 版本冲突"来使用本工具。',
           mbInformation, MB_OK);
  end;
end;
