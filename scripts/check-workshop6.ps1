$ErrorActionPreference = 'Continue'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend/restaurant-frontend-app'
$reportRoot = Join-Path $repositoryRoot 'diagnostics/workshop6'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$env:BACKEND_URL = 'https://0xnay410zf.execute-api.us-west-2.amazonaws.com/develop'
$env:CHROME_BIN = Join-Path $env:ProgramFiles 'Google/Chrome/Application/chrome.exe'
if (-not (Test-Path -LiteralPath $env:CHROME_BIN)) {
    $env:CHROME_BIN = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
}

Push-Location -LiteralPath $frontendRoot
try {
    & node node_modules/typescript/bin/tsc -p tsconfig.app.json --noEmit 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'app-types.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Application type check failed.' }
    & node node_modules/typescript/bin/tsc -p tsconfig.spec.json --noEmit 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'spec-types.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Spec type check failed.' }
    & npm.cmd run test:hw -- --browsers=ChromeHeadless --progress=false 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'tests.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Homework tests failed. See diagnostics/workshop6/tests.txt.' }
    & npm.cmd run build 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'build.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Production build failed. See diagnostics/workshop6/build.txt.' }
    Write-Host 'All four workshop 6 checks passed.'
} finally {
    Pop-Location
}
