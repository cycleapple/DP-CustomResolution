@set src=.\CustomResolution\bin\x64\Release\CustomResolution
@set dst=O:\home\ade\wwwext\xiv\CustomResolution

dotnet build -c Release

rmdir /s /q %dst%
xcopy %src%\ %dst%\ /E
xcopy .\CustomResolution\images\icon.png %dst%\

pause