MSBuild Thingie.Tracking.csproj /t:Rebuild /p:Configuration=Release
nuget.exe pack Thingie.Tracking.nuspec -OutputDirectory %NUGET_LOCAL%