Option Explicit

Dim shell, fso, root, launcher, ps, command, i

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = root & "\scripts\visual-agent\agent-launcher.ps1"
ps = shell.ExpandEnvironmentStrings("%WINDIR%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"

Function Q(value)
  Q = Chr(34) & Replace(CStr(value), Chr(34), Chr(34) & Chr(34)) & Chr(34)
End Function

Function JoinArgs()
  Dim result, j
  result = ""
  For j = 0 To WScript.Arguments.Count - 1
    If Len(result) > 0 Then
      result = result & " "
    End If
    result = result & Q(WScript.Arguments(j))
  Next
  JoinArgs = result
End Function

Function IsAdmin()
  IsAdmin = (shell.Run(shell.ExpandEnvironmentStrings("%COMSPEC%") & " /c fltmc >nul 2>&1", 0, True) = 0)
End Function

If Not IsAdmin() Then
  Dim app, elevatedArgs
  Set app = CreateObject("Shell.Application")
  elevatedArgs = Q(WScript.ScriptFullName)
  If Len(JoinArgs()) > 0 Then
    elevatedArgs = elevatedArgs & " " & JoinArgs()
  End If
  app.ShellExecute WScript.FullName, elevatedArgs, root, "runas", 0
  WScript.Quit
End If

command = Q(ps) & " -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Q(launcher)

If WScript.Arguments.Count = 0 Then
  command = command & " -Action Gui"
Else
  For i = 0 To WScript.Arguments.Count - 1
    command = command & " " & Q(WScript.Arguments(i))
  Next
End If

shell.CurrentDirectory = root
shell.Run command, 0, False
