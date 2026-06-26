; NetThrottle NSIS installer script
; Build:  makensis -DVERSION=1.2.3 -DSRC_DIR=..\publish -DOUT_DIR=..\dist Setup.nsi
; Produces: <OUT_DIR>\NetThrottle_v<VERSION>_Setup.exe

Unicode true

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef SRC_DIR
  !define SRC_DIR "..\publish"     ; folder containing the published payload
!endif
!ifndef OUT_DIR
  !define OUT_DIR "."
!endif

!define PRODUCT_NAME "NetThrottle"
!define PRODUCT_PUBLISHER "Kim, Hwan"
!define PRODUCT_WEB_SITE "https://github.com/akon47/NetThrottle"
!define APP_EXE "NetThrottle.exe"
; Must match the named mutex in App.xaml.cs so the installer can wait for a running instance.
!define APP_MUTEX "Global\NetThrottle.SingleInstance.{F1C6E2B8-7A4E-4C2E-9E4B-2B8D5C9A1F30}"

!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\${APP_EXE}"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

!include "MUI2.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"
!insertmacro VersionCompare

Name "${PRODUCT_NAME} ${VERSION}"
OutFile "${OUT_DIR}\NetThrottle_v${VERSION}_Setup.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel admin
ShowInstDetails show
ShowUnInstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}.0"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (c) ${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"

Var NeedUninstall

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Korean"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "NetThrottle requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64

  ; Wait for a running instance to close (mutex handshake with the app / updater).
  StrCpy $R0 0
  app_wait_loop:
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${APP_MUTEX}") p .r0'
    IntPtrCmp $0 0 app_wait_done
    System::Call 'kernel32::CloseHandle(p r0)'
    Sleep 300
    IntOp $R0 $R0 + 1
    IntCmp $R0 33 app_wait_kill app_wait_loop app_wait_kill
  app_wait_kill:
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE} /T'
    Sleep 800
  app_wait_done:

  ; Offer to remove a previous version first.
  ReadRegStr $0 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $0 "" done
  ReadRegStr $1 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion"
  StrCpy $NeedUninstall 1
  done:
FunctionEnd

Section "NetThrottle" SEC01
  StrCmp $NeedUninstall 1 0 +3
    DetailPrint "Removing previous version..."
    ExecWait '"$0" /S _?=$INSTDIR'

  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "${SRC_DIR}\${APP_EXE}"
  File "${SRC_DIR}\WinDivert.dll"
  File "${SRC_DIR}\WinDivert64.sys"

  SetOutPath "$INSTDIR\locales"
  File "${SRC_DIR}\locales\*.json"
  SetOutPath "$INSTDIR"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\uninst.exe"
  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\${APP_EXE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  SetRegView 64
  ; Wait for / close a running instance before uninstalling.
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${APP_MUTEX}") p .r0'
  IntPtrCmp $0 0 +3
    System::Call 'kernel32::CloseHandle(p r0)'
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE} /T'
FunctionEnd

Section Uninstall
  Delete "$INSTDIR\uninst.exe"
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\WinDivert.dll"
  Delete "$INSTDIR\WinDivert64.sys"
  Delete "$INSTDIR\locales\*.json"
  RMDir "$INSTDIR\locales"

  Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  RMDir "$INSTDIR"

  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
  SetAutoClose true
SectionEnd
