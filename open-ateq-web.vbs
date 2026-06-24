Option Explicit

Dim shell, fso, scriptDir, startupScript, webUrl, command, result

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
startupScript = fso.BuildPath(scriptDir, "start-server-bg.ps1")
webUrl = "http://127.0.0.1:3000/"

If Not fso.FileExists(startupScript) Then
    MsgBox "Cannot find start-server-bg.ps1 in:" & vbCrLf & scriptDir, vbCritical, "ATEQ Leak Test"
    WScript.Quit 1
End If

command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File " & Quote(startupScript)
result = shell.Run(command, 0, True)

If result <> 0 Then
    MsgBox "ATEQ service failed to start. Please check server.log or server_error.log.", vbCritical, "ATEQ Leak Test"
    WScript.Quit result
End If

shell.Run "cmd /c start """" """ & webUrl & """", 0, False

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
