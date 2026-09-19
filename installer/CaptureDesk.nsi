Unicode True
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!ifndef PAYLOAD
  !error "Pass /DPAYLOAD, /DOUTPUT and /DUNINSTALL_FILES to makensis"
!endif
Name "CaptureDesk 0.4"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\CaptureDesk"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "InstallLocation"
RequestExecutionLevel user
ManifestDPIAware True
SetCompressor /SOLID lzma
SetCompressorDictSize 64
VIProductVersion "0.4.0.0"
VIAddVersionKey "ProductName" "CaptureDesk"
VIAddVersionKey "FileDescription" "CaptureDesk 安装程序"
VIAddVersionKey "FileVersion" "0.4"
VIAddVersionKey "LegalCopyright" "CaptureDesk contributors"
!define MUI_ICON "..\src\CaptureDesk.App\Assets\Brand\CaptureDesk.ico"
!define MUI_UNICON "..\src\CaptureDesk.App\Assets\Brand\CaptureDesk.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\CaptureDesk.App.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  SetShellVarContext current
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "CaptureDesk 需要 64 位 Windows 10 或 Windows 11。"
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentMajorVersionNumber"
  ReadRegStr $1 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 10
  ${OrIf} $1 < 19045
    MessageBox MB_ICONSTOP "CaptureDesk 需要 Windows 10 22H2 或更新版本。"
    Abort
  ${EndIf}
FunctionEnd

Section "CaptureDesk（必需，含 .NET 运行环境）" Main
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\CaptureDesk"
  CreateShortcut "$SMPROGRAMS\CaptureDesk\CaptureDesk.lnk" "$INSTDIR\CaptureDesk.App.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "DisplayName" "CaptureDesk"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "DisplayVersion" "0.4"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "Publisher" "CaptureDesk contributors"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "DisplayIcon" "$INSTDIR\CaptureDesk.App.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk" "NoRepair" 1
SectionEnd

Section /o "桌面快捷方式" Desktop
  CreateShortcut "$DESKTOP\CaptureDesk.lnk" "$INSTDIR\CaptureDesk.App.exe"
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  !include "${UNINSTALL_FILES}"
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CaptureDesk"
  ${If} $0 == '$\"$INSTDIR\CaptureDesk.App.exe$\" --background'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CaptureDesk"
  ${EndIf}
  Delete "$DESKTOP\CaptureDesk.lnk"
  Delete "$SMPROGRAMS\CaptureDesk\CaptureDesk.lnk"
  RMDir "$SMPROGRAMS\CaptureDesk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CaptureDesk"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
