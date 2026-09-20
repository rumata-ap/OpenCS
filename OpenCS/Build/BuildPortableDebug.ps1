[CmdletBinding()]
param(
    [switch]$NoRestore
)

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repositoryRoot 'OpenCS.sln'
$arguments = @('build', $solution, '-p:BundleTools=true', '-m:1')
if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
exit $LASTEXITCODE
