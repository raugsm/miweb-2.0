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
