Option Explicit

Dim shell, fso, root, exePath, args, i, distPath, folder, newestDate, candidate

Set shell = CreateObject("Shell.Application")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = ""
distPath = root & "\desktop-agent\dist"
newestDate = #1/1/1970#

If fso.FolderExists(distPath) Then
  For Each folder In fso.GetFolder(distPath).SubFolders
    If LCase(Left(folder.Name, Len("AriadGSMAgent-next"))) = LCase("AriadGSMAgent-next") Then
      candidate = folder.Path & "\AriadGSM Agent.exe"
      If fso.FileExists(candidate) And folder.DateLastModified > newestDate Then
        newestDate = folder.DateLastModified
        exePath = candidate
      End If
    End If
  Next
End If

If Len(exePath) = 0 Then
  exePath = root & "\desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe"
End If

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
