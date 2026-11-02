param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{12}$')]
    [string]$AccountId
)

$ErrorActionPreference = 'Stop'
$workshopRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backendRoot = Join-Path $workshopRoot 'backend/restaurant-backend-app'
$infraRoot = Join-Path $workshopRoot 'infra/terraform'
$syndicateTemplateRoot = Join-Path $backendRoot '.syndicate-config-restaurant-backend-app'
$syndicateRoot = Join-Path $backendRoot '.syndicate-config-local'
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath (Join-Path $backendRoot 'restaurant-backend-app.sln'))) {
    throw 'Run this script from a complete restaurant-workshops checkout.'
}

# Local configuration only. This script does not create or change AWS resources.
$configPath = Join-Path $syndicateRoot 'syndicate.yml'
$aliasesPath = Join-Path $syndicateRoot 'syndicate_aliases.yml'
$configText = [System.IO.File]::ReadAllText((Join-Path $syndicateTemplateRoot 'syndicate.yml'))
$aliasesText = [System.IO.File]::ReadAllText((Join-Path $syndicateTemplateRoot 'syndicate_aliases.yml'))
$configText = $configText.Replace('{your_account_id}', $AccountId).Replace('your_account_id', $AccountId)
$aliasesText = $aliasesText.Replace('{your_account_id}', $AccountId).Replace('your_account_id', $AccountId)
$projectPathYaml = "'" + $backendRoot.Replace('\', '/').Replace("'", "''") + "'"
$configText = [regex]::Replace($configText, '(?m)^project_path:.*$', "project_path: $projectPathYaml")

if ($configText -notmatch ('(?m)^account_id:\s*"' + $AccountId + '"') -or
    $aliasesText -notmatch ('(?m)^account_id:\s*"' + $AccountId + '"')) {
    throw 'Configuration already names a different account. Review it before switching accounts.'
}

New-Item -ItemType Directory -Path $syndicateRoot -Force | Out-Null
[System.IO.File]::WriteAllText($configPath, $configText, $utf8WithoutBom)
[System.IO.File]::WriteAllText($aliasesPath, $aliasesText, $utf8WithoutBom)

# An init override avoids changing the original Terraform backend declaration.
$backendOverride = "bucket = `"restaurant-terraform-state-$AccountId`"`n"
[System.IO.File]::WriteAllText((Join-Path $infraRoot 'backend.local.hcl'), $backendOverride, $utf8WithoutBom)
$variablesJson = @{ account_id = $AccountId; aws_region = 'us-west-2' } | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $infraRoot 'workshop.auto.tfvars.json'), $variablesJson, $utf8WithoutBom)

Write-Host 'Local configuration prepared for us-west-2. No AWS resources were changed.'
Write-Host 'After creating the Terraform state bucket as described in README.md:'
Write-Host "  Set-Location '$infraRoot'"
Write-Host '  terraform init "-backend-config=backend.local.hcl"'
Write-Host '  terraform fmt -check -recursive'
Write-Host '  terraform validate'
Write-Host '  terraform plan'
Write-Host 'Review the plan before applying it. Then follow the backend and frontend deployment steps in README.md.'
