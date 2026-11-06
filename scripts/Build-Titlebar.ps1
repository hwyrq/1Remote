[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet',
    [string[]]$PackageSource = @('https://api.nuget.org/v3/index.json'),
    [string]$ArtifactsPath,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$titlebarRepo = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $titlebarRepo 'artifacts' }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
$titlebarBuild = Join-Path $ArtifactsPath 'build'
$titlebarPublish = Join-Path $ArtifactsPath '1Remote'
$titlebarSdk = (Get-Command $DotnetPath -ErrorAction Stop).Source
$titlebarProject = Join-Path $titlebarRepo 'Ui\Ui.csproj'
$titlebarHooks = Join-Path $PSScriptRoot 'Titlebar-Build.targets'
$titlebarNuget = Join-Path $titlebarBuild 'NuGet.Config'
$titlebarSavedEnvironment = @{}
$titlebarEnvironment = @{
    DOTNET_ROOT = Split-Path -Parent $titlebarSdk
    DOTNET_CLI_HOME = Join-Path $titlebarBuild 'dotnet-home'
    NUGET_PACKAGES = Join-Path $titlebarBuild 'nuget-packages'
    NUGET_HTTP_CACHE_PATH = Join-Path $titlebarBuild 'nuget-http-cache'
    TEMP = Join-Path $titlebarBuild 'temp'
    TMP = Join-Path $titlebarBuild 'temp'
    APPDATA = Join-Path $titlebarBuild 'appdata'
    LOCALAPPDATA = Join-Path $titlebarBuild 'localappdata'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    MSBuildEnableWorkloadResolver = 'false'
}

foreach ($titlebarDirectory in @($titlebarBuild, $titlebarPublish,
        $titlebarEnvironment.DOTNET_CLI_HOME, $titlebarEnvironment.NUGET_PACKAGES,
        $titlebarEnvironment.TEMP, $titlebarEnvironment.APPDATA, $titlebarEnvironment.LOCALAPPDATA)) {
    New-Item -ItemType Directory -Path $titlebarDirectory -Force | Out-Null
}

# Supplying a downloaded package cache here uses it as a read-only local feed.
$titlebarSources = for ($titlebarIndex = 0; $titlebarIndex -lt $PackageSource.Count; $titlebarIndex++) {
    $titlebarSource = [Security.SecurityElement]::Escape($PackageSource[$titlebarIndex])
    "    <add key=`"source$titlebarIndex`" value=`"$titlebarSource`" />"
}
$titlebarConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
$($titlebarSources -join [Environment]::NewLine)
  </packageSources>
</configuration>
"@
[IO.File]::WriteAllText($titlebarNuget, $titlebarConfig, [Text.UTF8Encoding]::new($false))

try {
    foreach ($titlebarName in $titlebarEnvironment.Keys) {
        $titlebarSavedEnvironment[$titlebarName] = [Environment]::GetEnvironmentVariable($titlebarName, 'Process')
        [Environment]::SetEnvironmentVariable($titlebarName, $titlebarEnvironment[$titlebarName], 'Process')
    }
    Push-Location $titlebarRepo
    try {
        $titlebarCommon = @('-r', 'win-x64', '-p:SelfContained=true', '-p:Configuration=Release',
            '-p:NuGetAudit=false', '-m:1', '-nr:false')
        & $titlebarSdk restore $titlebarProject --configfile $titlebarNuget @titlebarCommon
        if ($LASTEXITCODE -ne 0) { throw "Package restore failed ($LASTEXITCODE)." }
        & $titlebarSdk publish $titlebarProject --no-restore -c Release -o $titlebarPublish @titlebarCommon `
            "-p:CustomAfterMicrosoftCommonTargets=$titlebarHooks" "-p:RestoreConfigFile=$titlebarNuget"
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed ($LASTEXITCODE)." }

        if ($Verify) {
            $titlebarQaProject = Join-Path $PSScriptRoot 'titlebar-smoke\TitlebarButtonSmoke.csproj'
            $titlebarQaOutput = Join-Path $ArtifactsPath 'qa\bin'
            $titlebarQaData = Join-Path $ArtifactsPath 'qa\data'
            & $titlebarSdk restore $titlebarQaProject --configfile $titlebarNuget @titlebarCommon
            if ($LASTEXITCODE -ne 0) { throw "Verification restore failed ($LASTEXITCODE)." }
            & $titlebarSdk build $titlebarQaProject --no-restore -c Release -o $titlebarQaOutput @titlebarCommon `
                "-p:TitlebarPublishDirectory=$titlebarPublish"
            if ($LASTEXITCODE -ne 0) { throw "Verification build failed ($LASTEXITCODE)." }
            foreach ($titlebarFile in Get-ChildItem -LiteralPath $titlebarPublish -Force) {
                Copy-Item -LiteralPath $titlebarFile.FullName -Destination $titlebarQaOutput -Recurse -Force
            }
            New-Item -ItemType Directory -Path $titlebarQaData -Force | Out-Null
            Push-Location $titlebarQaData
            try {
                & (Join-Path $titlebarQaOutput 'TitlebarButtonSmoke.exe') |
                    Tee-Object -FilePath (Join-Path $ArtifactsPath 'qa\verification.log')
                if ($LASTEXITCODE -ne 0) { throw "Interaction verification failed ($LASTEXITCODE)." }
            }
            finally { Pop-Location }
        }
        Write-Host "Published: $titlebarPublish"
    }
    finally { Pop-Location }
}
finally {
    foreach ($titlebarName in $titlebarSavedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($titlebarName, $titlebarSavedEnvironment[$titlebarName], 'Process')
    }
}
