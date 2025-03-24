@set src=.\CustomResolution2782\bin\x64\Release\CustomResolution2782
@set dst=O:\home\ade\wwwext\xiv\CustomResolution2782

dotnet build -c Release

rmdir /s /q %dst%
xcopy %src%\ %dst%\ /E
xcopy .\CustomResolution2782\images\icon.png %dst%\

pause
