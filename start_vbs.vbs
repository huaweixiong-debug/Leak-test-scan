
Set WshShell = CreateObject("WScript.Shell")
Set Fso = CreateObject("Scripting.FileSystemObject")
ScriptDir = Fso.GetParentFolderName(WScript.ScriptFullName)
WshShell.CurrentDirectory = ScriptDir
WshShell.Run """" & ScriptDir & "\runtime18\node-v18.20.8-win-x64\node.exe"" server.js >> ""server.out"" 2>>""server.err""", 0, False
