$ErrorActionPreference = 'Stop'

$launcher = Get-Command py.exe -ErrorAction SilentlyContinue

$pythonExe = $null
$pythonArgs = @()
$registeredVersions = @(
    Get-ChildItem HKCU:\Software\Python\PythonCore,HKLM:\Software\Python\PythonCore `
        -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName
)
foreach ($candidateName in @('python.exe', 'python3.exe')) {
    $candidate = Get-Command $candidateName -ErrorAction SilentlyContinue
    if ($null -ne $candidate) {
        & $candidate.Source -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 9) else 1)" 1>$null 2>$null
        if ($LASTEXITCODE -eq 0) {
            $pythonExe = $candidate.Source
            break
        }
    }
}

if ($null -eq $pythonExe -and $null -ne $launcher -and
    ($registeredVersions.Count -eq 0 -or ($registeredVersions -match '^3\.(9|1[0-9])$'))) {
    foreach ($version in @('3.12', '3.11', '3.10', '3.9')) {
        & $launcher.Source "-$version" -c "import sys; print(sys.version_info[:2])" 1>$null 2>$null
        if ($LASTEXITCODE -eq 0) {
            $pythonExe = $launcher.Source
            $pythonArgs = @("-$version")
            break
        }
    }
}

if ($null -eq $pythonExe) {
    $registered = $registeredVersions -join ', '
    throw "Python 3.9+ не найден. Зарегистрированные версии: [$registered]. Проверьте py -0p, python --version и установку для текущего пользователя."
}

$venv = Join-Path $PSScriptRoot '.venv'
if (-not (Test-Path (Join-Path $venv 'Scripts\python.exe'))) {
    & $pythonExe @pythonArgs -m venv $venv
}

$venvPython = Join-Path $venv 'Scripts\python.exe'
& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install -e (Join-Path $PSScriptRoot 'structural-materials')
& $venvPython -m pip install pytest

Write-Host "Готово: $venvPython"
