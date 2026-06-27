# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/p3rpc.expGainEditor/*" -Force -Recurse -ErrorAction SilentlyContinue
dotnet publish "./p3rpc.expGainEditor.csproj" -c Release -o "$env:RELOADEDIIMODS/p3rpc.expGainEditor" /p:OutputPath="./bin/Release"

# Restore Working Directory
Pop-Location
