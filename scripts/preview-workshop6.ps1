$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend/restaurant-frontend-app'
$reportRoot = Join-Path $repositoryRoot 'diagnostics/workshop6'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$env:AWS_PAGER = ''
$account = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne '054776129200') {
    throw 'Cannot verify the expected AWS account.'
}

Push-Location -LiteralPath $frontendRoot
try {
    $ErrorActionPreference = 'Continue'
    & node node_modules/@angular/cli/bin/ng.js build '--configuration=development' '--base-href=/preview/workshop6/' '--output-path=dist/workshop6-preview' 2>&1 |
        Tee-Object -FilePath (Join-Path $reportRoot 'preview-build.txt')
    $buildExitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($buildExitCode -ne 0) { throw 'Preview build failed; nothing uploaded.' }
    & aws s3 sync dist/workshop6-preview/browser s3://restaurant-frontend-054776129200/preview/workshop6/ --region us-west-2
    if ($LASTEXITCODE -ne 0) { throw 'Preview upload failed.' }
    # The existing distribution has a root-level SPA fallback. Store the exact
    # preview route as HTML so refreshing this route serves the preview index.
    & aws s3 cp dist/workshop6-preview/browser/index.html s3://restaurant-frontend-054776129200/preview/workshop6/reservations/demo-reservation/group-order --content-type text/html --cache-control no-cache --region us-west-2
    if ($LASTEXITCODE -ne 0) { throw 'Preview route upload failed.' }
    $distributionId = & aws cloudfront list-distributions --query "DistributionList.Items[?DomainName=='d2ebas3eqwfhw7.cloudfront.net'].Id | [0]" --output text
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($distributionId) -or $distributionId.Trim() -eq 'None') {
        throw 'Cannot find the preview CloudFront distribution.'
    }
    $invalidationId = & aws cloudfront create-invalidation --distribution-id $distributionId.Trim() --paths '/preview/workshop6/*' --query Invalidation.Id --output text
    if ($LASTEXITCODE -ne 0) { throw 'Preview cache invalidation failed.' }
    Write-Host 'Waiting for the preview cache to refresh...'
    & aws cloudfront wait invalidation-completed --distribution-id $distributionId.Trim() --id $invalidationId.Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Preview cache refresh has not completed yet.' }
    Write-Host 'Preview ready:'
    Write-Host 'https://d2ebas3eqwfhw7.cloudfront.net/preview/workshop6/reservations/demo-reservation/group-order?demo'
    Write-Host 'No Git push was performed. Root website files were not replaced.'
} finally {
    Pop-Location
}
