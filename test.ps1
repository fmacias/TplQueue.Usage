param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$TplQueuePackageVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configFile = Join-Path $root "NuGet.config"
$sampleProjects = @(
    (Join-Path $root "samples\TplQueue.Sample.Etl.Contracts\TplQueue.Sample.Etl.Contracts.csproj"),
    (Join-Path $root "samples\TplQueue.Sample.Etl\TplQueue.Sample.Etl.csproj"),
    (Join-Path $root "samples\PackageConsumptionSmokeConsole\PackageConsumptionSmokeConsole.csproj"),
    (Join-Path $root "samples\QueueObserverConsole\QueueObserverConsole.csproj"),
    (Join-Path $root "samples\QueueObserverSignalRDashboard\QueueObserverSignalRDashboard.csproj"),
    (Join-Path $root "samples\TplQueue.Sample.BlazorSignalR\TplQueue.Sample.BlazorSignalR.csproj")
)
$testProject = Join-Path $root "test\integration\Fmacias.TplQueue.Usage.Integration.Test\Fmacias.TplQueue.Usage.Integration.Test.csproj"

$versionArgs = @()
if ($TplQueuePackageVersion) {
    $versionArgs += "/p:TplQueuePackageVersion=$TplQueuePackageVersion"
}

foreach ($sampleProject in $sampleProjects) {
    dotnet restore $sampleProject --configfile $configFile @versionArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    dotnet build $sampleProject --configuration $Configuration --no-restore @versionArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

dotnet restore $testProject --configfile $configFile @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test $testProject --configuration $Configuration --no-restore @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
