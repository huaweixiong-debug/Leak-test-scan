
Set WshShell = CreateObject("WScript.Shell")
Set Fso = CreateObject("Scripting.FileSystemObject")
ScriptDir = Fso.GetParentFolderName(WScript.ScriptFullName)
WshShell.CurrentDirectory = ScriptDir
Quote = Chr(34)
NodePath = ScriptDir & "\runtime18\node-v18.20.8-win-x64\node.exe"
Command = "cmd.exe /c " & Quote & Quote & NodePath & Quote & " server.js 1>>" & Quote & ScriptDir & "\server.out" & Quote & " 2>>" & Quote & ScriptDir & "\server.err" & Quote & Quote
WshShell.Run Command, 0, False
