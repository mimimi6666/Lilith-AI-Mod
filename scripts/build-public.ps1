[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [switch] $SkipGamePlugins
)
$ErrorActionPreference = 'Stop'
$publicSourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projects = @(
    'src/LilithRuntime/LilithRuntime.csproj',
    'src/LilithControlCenter/LilithControlCenter.csproj',
    'src/LilithLocalSceneCapture/LilithLocalSceneCapture.csproj',
    'src/LilithVoiceHost/LilithVoiceHost.csproj'
)
if (-not $SkipGamePlugins) {
    $projects += @(
        'src/LilithTextInjector/LilithTextInjector.csproj',
        'src/LilithCursorGuard/LilithCursorGuard.csproj'
    )
}
Push-Location $publicSourceRoot
try {
    foreach ($project in $projects) {
        & dotnet build $project --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    Write-Output 'Selected projects built. No application was installed, deployed or started.'
} finally {
    Pop-Location
}
