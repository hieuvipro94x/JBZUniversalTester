$ErrorActionPreference='Stop'
dotnet publish .\JBZLicenseIssuer.csproj -c Release -r win-x86 --self-contained false -o .\Publish\V1.0.0
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Write-Output 'Published to Publish\V1.0.0. Sign EXE with the administrator code-signing workflow before deployment.'
