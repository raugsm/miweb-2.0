Option Explicit

Dim shell, fso, root, exePath, args, i

Set shell = CreateObject("Shell.Application")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = root & "\desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe"

If Not fso.FileExists(exePath) Then
  exePath = root & "\desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\bin\Debug\net10.0-windows\AriadGSM Agent.exe"
End If

If Not fso.FileExists(exePath) Then
  MsgBox "AriadGSM Agent.exe no existe todavia. Ejecuta desktop-agent\windows-app\build-agent-package.cmd.", 48, "AriadGSM Agent"
  WScript.Quit 1
End If

args = ""
For i = 0 To WScript.Arguments.Count - 1
  If Len(args) > 0 Then args = args & " "
  args = args & Chr(34) & Replace(WScript.Arguments(i), Chr(34), Chr(34) & Chr(34)) & Chr(34)
Next

shell.ShellExecute exePath, args, root, "open", 1
