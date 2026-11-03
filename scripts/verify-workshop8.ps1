param(
    [Parameter(Mandatory = $true)][string]$ExpectedAccountId,
    [Parameter(Mandatory = $true)][string]$FrontendBucket,
    [Parameter(Mandatory = $true)][string]$CloudFrontDomain,
    [string]$Region = 'us-west-2'
)

$ErrorActionPreference = 'Continue'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend/restaurant-frontend-app'
$frameworkRoot = Join-Path $repositoryRoot 'aqa/restaurant-aqa-framework'
$reportRoot = Join-Path $repositoryRoot 'diagnostics/workshop8'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$env:AWS_PAGER = ''

# Read only target URLs from the private configuration; never print credentials.
Push-Location -LiteralPath $frameworkRoot
try {
    $targetJson = & node --input-type=module -e "process.loadEnvFile('.env'); console.log(JSON.stringify({api:process.env.API_BASE_URL,base:process.env.BASE_URL}));"
    if ($LASTEXITCODE -ne 0) { throw 'Private AQA configuration is missing or invalid.' }
    $target = $targetJson | ConvertFrom-Json
} finally { Pop-Location }
if (([Uri]$target.base).Host -ne $CloudFrontDomain) { throw 'CloudFront domain does not match the configured frontend.' }
$account = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne $ExpectedAccountId) { throw 'AWS account mismatch.' }
$distributionsJson = & aws cloudfront list-distributions --output json --no-cli-pager
if ($LASTEXITCODE -ne 0) { throw 'Cannot discover CloudFront.' }
$matches = @(($distributionsJson | ConvertFrom-Json).DistributionList.Items | Where-Object { $_.DomainName -eq $CloudFrontDomain })
if ($matches.Count -ne 1 -or -not ($matches[0].Origins.Items.DomainName | Where-Object { $_.StartsWith("$FrontendBucket.s3.") })) {
    throw 'Cannot verify the expected frontend S3 origin. Nothing was uploaded.'
}

$env:BACKEND_URL = $target.api
$chrome = Join-Path $env:ProgramFiles 'Google/Chrome/Application/chrome.exe'
if (Test-Path -LiteralPath $chrome) { $env:CHROME_BIN = $chrome }
Push-Location -LiteralPath $frontendRoot
try {
    foreach ($configuration in @('tsconfig.app.json', 'tsconfig.spec.json')) {
        & node node_modules/typescript/bin/tsc -p $configuration --noEmit
        if ($LASTEXITCODE -ne 0) { throw 'Frontend type checks failed.' }
    }
    & npm.cmd run test:hw -- --browsers=ChromeHeadless --progress=false 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'homework-tests.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Owner feature regression tests failed.' }
    & node node_modules/@angular/cli/bin/ng.js test --watch=false --browsers=ChromeHeadless --include=src/app/auth/token.interceptor.spec.ts --progress=false 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'auth-tests.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Owner authentication regression tests failed.' }
    & npm.cmd run build 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'frontend-build.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed. Nothing was uploaded.' }
    Write-Host 'Publishing the verified Owner authentication correction...'
    & aws s3 sync dist/restaurant-frontend-app/browser "s3://$FrontendBucket/" --region $Region
    if ($LASTEXITCODE -ne 0) { throw 'Frontend upload failed.' }
    $invalidation = & aws cloudfront create-invalidation --distribution-id $matches[0].Id --paths '/*' --query Invalidation.Id --output text
    if ($LASTEXITCODE -ne 0) { throw 'CloudFront invalidation failed.' }
    Write-Host 'Waiting for CloudFront cache refresh...'
    & aws cloudfront wait invalidation-completed --distribution-id $matches[0].Id --id $invalidation.Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cache refresh is pending; rerun verification after it completes.' }
} finally { Pop-Location }

Push-Location -LiteralPath $frameworkRoot
try {
    & npm.cmd run check
    if ($LASTEXITCODE -ne 0) { throw 'AQA static checks failed.' }
    & npm.cmd run test:list
    if ($LASTEXITCODE -ne 0) { throw 'AQA test discovery failed.' }
    & npm.cmd test 2>&1 | Tee-Object -FilePath (Join-Path $reportRoot 'e2e.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Some E2E tests failed. Keep the HTML report and diagnostics/workshop8/e2e.txt for review.' }
    Write-Host 'All selected E2E scenarios passed. Reports are in aqa/restaurant-aqa-framework/playwright-report.'
    Write-Host 'No Git push was performed.'
} finally { Pop-Location }
