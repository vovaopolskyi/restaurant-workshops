$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend/restaurant-frontend-app'
$backendRoot = Join-Path $repositoryRoot 'backend/restaurant-backend-app'
$env:AWS_PAGER = ''

& (Join-Path $PSScriptRoot 'check-workshop6.ps1')
if (-not $?) { throw 'Verification failed. GitHub and AWS were not updated.' }

$account = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne '054776129200') {
    throw 'Cannot verify the expected AWS account.'
}
& git -C $repositoryRoot push -u origin feature/workshop-06-owner-screen
if ($LASTEXITCODE -ne 0) { throw 'Push failed. AWS was not updated.' }

Write-Host 'Updating backend read-only invitation support...'
& (Join-Path $backendRoot 'workshop5.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Backend update failed. Frontend was not uploaded.' }

$bundleRoot = Join-Path $frontendRoot 'dist/restaurant-frontend-app/browser'
& aws s3 sync $bundleRoot s3://restaurant-frontend-054776129200/ --region us-west-2
if ($LASTEXITCODE -ne 0) { throw 'Production frontend upload failed.' }

$distributionId = & aws cloudfront list-distributions --query "DistributionList.Items[?DomainName=='d2ebas3eqwfhw7.cloudfront.net'].Id | [0]" --output text
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($distributionId) -or $distributionId.Trim() -eq 'None') {
    throw 'Cannot locate CloudFront distribution.'
}
$invalidationId = & aws cloudfront create-invalidation --distribution-id $distributionId.Trim() --paths '/*' --query Invalidation.Id --output text
if ($LASTEXITCODE -ne 0) { throw 'CloudFront cache invalidation failed.' }
Write-Host 'Waiting for the production cache to refresh...'
& aws cloudfront wait invalidation-completed --distribution-id $distributionId.Trim() --id $invalidationId.Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cache refresh is still pending.' }
Write-Host 'Workshop 6 published to GitHub and AWS.'
Write-Host 'Production (real login): https://d2ebas3eqwfhw7.cloudfront.net'
Write-Host 'Demo preview: https://d2ebas3eqwfhw7.cloudfront.net/preview/workshop6/reservations/demo-reservation/group-order?demo'
