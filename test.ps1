param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$TplQueuePackageVersion = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configFile = Join-Path $root "NuGet.config"
$sampleProject = Join-Path $root "samples\QueueObserverConsole\QueueObserverConsole.csproj"
$testProject = Join-Path $root "test\integration\Fmacias.TplQueue.Usage.Integration.Test\Fmacias.TplQueue.Usage.Integration.Test.csproj"

$versionArgs = @()
if ($TplQueuePackageVersion) {
    $versionArgs += "/p:TplQueuePackageVersion=$TplQueuePackageVersion"
}

dotnet restore $sampleProject --configfile $configFile @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build $sampleProject --configuration $Configuration --no-restore @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet restore $testProject --configfile $configFile @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test $testProject --configuration $Configuration --no-restore @versionArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
