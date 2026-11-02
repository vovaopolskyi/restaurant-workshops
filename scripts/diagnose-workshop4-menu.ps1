$ErrorActionPreference = 'Stop'
$env:AWS_PAGER = ''
$workshopRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$diagnosticsRoot = Join-Path $workshopRoot 'diagnostics'
New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
$reportPath = Join-Path $diagnosticsRoot 'menu.txt'

& {
    Write-Output ('Time: ' + (Get-Date -Format o))
    $account = & aws sts get-caller-identity --query Account --output text
    if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne '054776129200') {
        throw 'Cannot verify the expected AWS account.'
    }

    foreach ($endpoint in @('dishes?sortBy=PopularityDescending', 'locations')) {
        Write-Output ("API: $endpoint")
        try {
            $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 45 `
                -Uri "https://0xnay410zf.execute-api.us-west-2.amazonaws.com/develop/$endpoint" `
                -Headers @{ Origin = 'https://d2ebas3eqwfhw7.cloudfront.net' }
            Write-Output ('HTTP: ' + [int]$response.StatusCode)
            Write-Output ('Access-Control-Allow-Origin: ' + $response.Headers['Access-Control-Allow-Origin'])
            Write-Output ('Body: ' + $response.Content)
        } catch {
            Write-Output ('Request failed: ' + $_.Exception.Message)
            if ($_.ErrorDetails.Message) { Write-Output $_.ErrorDetails.Message }
        }
    }

    foreach ($tableName in @('Dishes', 'Locations')) {
        Write-Output ("DynamoDB table: $tableName (count only)")
        & aws dynamodb scan --table-name $tableName --select COUNT --consistent-read --region us-west-2 --output json
        if ($LASTEXITCODE -ne 0) { Write-Output 'DynamoDB count failed; see error above.' }
    }

    Write-Output 'Recent api-handler Lambda logs:'
    & aws logs tail /aws/lambda/api-handler --since 10m --format short --region us-west-2
    if ($LASTEXITCODE -ne 0) { Write-Output 'CloudWatch log retrieval failed; see error above.' }
} 2>&1 | Tee-Object -FilePath $reportPath

Write-Host "Report saved: $reportPath"
