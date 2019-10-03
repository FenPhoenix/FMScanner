echo ------------------------- START OF post_build.bat

rem ~ strips surrounded quotes if they exist
rem batch file hell #9072: no spaces can exist around = sign for these lines
set ConfigurationName=%~1
set TargetDir=%~2
set ProjectDir=%~3
set SolutionDir=%~4

xcopy "%SolutionDir%\libs\x86\7z.dll" "%TargetDir%" /y

rem Dumb hack to get rid of the JetBrains.Annotations.dll because ludicrously
rem xcopy requires you to make an entire file just to list excludes, rather than
rem specifying them on the command line like someone who is not clinically insane
del /F "%TargetDir%JetBrains.Annotations.dll"