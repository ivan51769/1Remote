param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalExe,
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\publish')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $OriginalExe -PathType Leaf)) { throw 'Original 1Remote executable is required for database compatibility.' }
Push-Location $PSScriptRoot
try {
    & $Dotnet run --project tools\BuildCompatibility\BuildCompatibility.csproj -- $OriginalExe (Join-Path $PSScriptRoot 'Ui\CompatibilitySalt.private')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to import the original build compatibility value.' }
    & $Dotnet publish Ui\Ui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host ('Published executable: ' + (Join-Path $OutputDirectory '1Remote.exe'))
}
finally { Pop-Location }
