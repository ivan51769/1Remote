param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\public-publish')
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Dotnet publish Ui\Ui.csproj -c Release -r win-x64 --self-contained true `
        -p:PublicPreviewRelease=true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false -p:XamlDebuggingInformation=false `
        -p:ContinuousIntegrationBuild=true "-p:PathMap=$PSScriptRoot=/_/" `
        -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Public preview build failed.' }
    Write-Host ('Published public executable: ' + (Join-Path $OutputDirectory '1Remote.exe'))
}
finally { Pop-Location }
