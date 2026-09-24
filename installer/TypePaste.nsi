; TypePaste installer (NSIS 3, Modern UI 2)
;
; Build with build/build.sh (Linux) or build/build.ps1 (Windows). Those scripts publish the app to
; artifacts/publish and generate installer/generated/*.nsh with the exact file list before calling makensis.
;
; Silent install:    "TypePaste Setup.exe" /S [/D=C:\Path\To\TypePaste]   (does not start TypePaste)
; Silent uninstall:  "C:\Program Files\TypePaste\Uninstall.exe" /S [/KEEPDATA]

Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\artifacts\publish"
!endif
!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "..\artifacts\TypePaste Setup.exe"
!endif

!define APP_NAME "TypePaste"
!define APP_EXE "TypePaste.exe"
!define PUBLISHER "TypePaste"
!define DESCRIPTION "Type prepared text into any app with one hotkey"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\TypePaste"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define MESSAGE_WINDOW_CLASS "TypePaste.MessageWindow"

Name "${APP_NAME}"
Caption "${APP_NAME} ${VERSION} Setup"
UninstallCaption "${APP_NAME} Uninstall"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails hide
ShowUninstDetails hide

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2026 ${PUBLISHER}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"

; ------------------------------------------------------------------ Branding (official TypePaste logo)
!define MUI_ICON "..\src\TypePaste\Assets\TypePaste.ico"
!define MUI_UNICON "..\src\TypePaste\Assets\TypePaste.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "branding\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "branding\welcome.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "branding\header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "branding\header.bmp"
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING

; ------------------------------------------------------------------ Pages
!define MUI_WELCOMEPAGE_TITLE "Welcome to TypePaste"
!define MUI_WELCOMEPAGE_TEXT "TypePaste types prepared text into any app with a single hotkey (F6) — for places where copy and paste don't work.$\r$\n$\r$\nEverything runs locally on this PC. No account, no internet connection and no extra downloads are needed.$\r$\n$\r$\nClick Next to continue."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
; TypePaste opens by itself as soon as the installation has finished.
!define MUI_PAGE_CUSTOMFUNCTION_SHOW LaunchTypePaste
!define MUI_FINISHPAGE_TITLE "TypePaste is ready"
!define MUI_FINISHPAGE_TEXT "TypePaste is installed and has been opened for you.$\r$\n$\r$\nPaste or type your text into TypePaste, click into any text field and press F6. Press Esc to stop.$\r$\n$\r$\nTypePaste stays available in the notification area."
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ------------------------------------------------------------------ Helpers

; Asks a running TypePaste to exit (it saves its settings), then force-closes it if needed.
!macro CLOSE_RUNNING_APP UN
Function ${UN}CloseRunningApp
  FindWindow $0 "${MESSAGE_WINDOW_CLASS}"
  ${If} $0 = 0
    Return
  ${EndIf}

  DetailPrint "Closing the running TypePaste..."
  SendMessage $0 ${WM_CLOSE} 0 0 /TIMEOUT=3000
  StrCpy $1 0
  ${While} $0 <> 0
    ${If} $1 >= 25
      ${Break}
    ${EndIf}
    Sleep 200
    IntOp $1 $1 + 1
    FindWindow $0 "${MESSAGE_WINDOW_CLASS}"
  ${EndWhile}

  ; Last resort for a hung instance, so its files can be replaced or removed.
  ${If} $0 <> 0
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM "${APP_EXE}"'
    Pop $0
    Sleep 500
  ${EndIf}
FunctionEnd
!macroend
!insertmacro CLOSE_RUNNING_APP ""
!insertmacro CLOSE_RUNNING_APP "un."

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "TypePaste requires 64-bit Windows 10 or Windows 11."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "TypePaste requires Windows 10 or Windows 11."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

; Starts TypePaste with normal user rights (the installer itself runs as administrator).
Function LaunchTypePaste
  ; The installer owns the foreground; let TypePaste bring its window to the front.
  System::Call 'user32::AllowSetForegroundWindow(i -1)'
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd

; Returns the user's Downloads folder in $0 (it can be moved, so ask Windows where it is).
Function GetDownloadsFolder
  StrCpy $0 ""
  System::Call 'shell32::SHGetKnownFolderPath(g "{374DE290-123F-4565-9164-39C4925E467B}", i 0, p 0, *p .r1) i .r2'
  ${If} $2 = 0
    System::Call '*$1(&w${NSIS_MAX_STRLEN} .r0)'
  ${EndIf}
  System::Call 'ole32::CoTaskMemFree(p r1)'
  ${If} $0 == ""
    StrCpy $0 "$PROFILE\Downloads"
  ${EndIf}
FunctionEnd

; ------------------------------------------------------------------ Install

Section "TypePaste (required)" SecApp
  SectionIn RO
  Call CloseRunningApp

  ; Upgrading: remove the files of the previous version first (settings are kept).
  ${If} ${FileExists} "$INSTDIR\Uninstall.exe"
    DetailPrint "Removing the previous version..."
    ExecWait '"$INSTDIR\Uninstall.exe" /S /KEEPDATA _?=$INSTDIR'
  ${EndIf}

  SetOutPath "$INSTDIR"
  !include "generated\install_files.nsh"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  SetShellVarContext all
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${DESCRIPTION}"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Comments" "${DESCRIPTION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Desktop shortcut" SecDesktop
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${DESCRIPTION}"
SectionEnd

Section "Downloads folder shortcut" SecDownloads
  Call GetDownloadsFolder
  CreateDirectory "$0"
  CreateShortcut "$0\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 SW_SHOWNORMAL "" "${DESCRIPTION}"
  ; Remembered so the uninstaller removes exactly this shortcut.
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DownloadsShortcut" "$0\${APP_NAME}.lnk"
SectionEnd

Section /o "Start TypePaste when I sign in" SecStartup
  WriteRegStr HKCU "${RUN_KEY}" "${APP_NAME}" '"$INSTDIR\${APP_EXE}" --tray'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The TypePaste application with everything it needs to run (no separate runtime required)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Adds a TypePaste shortcut to the desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDownloads} "Adds a TypePaste shortcut to your Downloads folder."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "Starts TypePaste quietly in the notification area when you sign in."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ------------------------------------------------------------------ Uninstall

Section "Uninstall"
  Call un.CloseRunningApp

  !include "generated\uninstall_files.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  SetShellVarContext all
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "DownloadsShortcut"
  ${If} $0 != ""
    Delete "$0"
  ${EndIf}
  DeleteRegKey HKLM "${UNINSTALL_KEY}"

  ; Per-user traces of the account running the uninstaller. /KEEPDATA is used when upgrading.
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/KEEPDATA" $R1
  ${If} ${Errors}
    DeleteRegValue HKCU "${RUN_KEY}" "${APP_NAME}"
    StrCpy $R2 "yes"
    ${IfNot} ${Silent}
      MessageBox MB_YESNO|MB_ICONQUESTION "Also remove your TypePaste settings and any saved text?" IDYES +2
      StrCpy $R2 "no"
    ${EndIf}
    ${If} $R2 == "yes"
      SetShellVarContext current
      RMDir /r "$APPDATA\${APP_NAME}"
      RMDir /r "$LOCALAPPDATA\${APP_NAME}"
    ${EndIf}
  ${EndIf}
SectionEnd
