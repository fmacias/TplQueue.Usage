param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$TplQueuePackageVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configFile = Join-Path $root "NuGet.config"
$projects = @(
    (Join-Path $root "samples\QueueObserverConsole\QueueObserverConsole.csproj"),
    (Join-Path $root "samples\QueueObserverSignalRDashboard\QueueObserverSignalRDashboard.csproj"),
    (Join-Path $root "test\integration\Fmacias.TplQueue.Usage.Integration.Test\Fmacias.TplQueue.Usage.Integration.Test.csproj")
)

$versionArgs = @()
if ($TplQueuePackageVersion) {
    $versionArgs += "/p:TplQueuePackageVersion=$TplQueuePackageVersion"
}

foreach ($project in $projects) {
    dotnet restore $project --configfile $configFile @versionArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $project --configuration $Configuration --no-restore @versionArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
