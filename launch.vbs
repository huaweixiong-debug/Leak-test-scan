Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = scriptDir
shell.Run "cmd /c """"" & scriptDir & "\runtime18\node-v18.20.8-win-x64\node.exe"""" ""server.js"" 1>>""" & scriptDir & "\server.out"" 2>>""" & scriptDir & "\server.err""""", 0, False
