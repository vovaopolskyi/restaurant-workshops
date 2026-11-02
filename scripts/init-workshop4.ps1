param(
    [ValidateSet('init', 'validate', 'plan', 'apply')]
    [string]$Action = 'init'
)

$ErrorActionPreference = 'Stop'

$infraRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../infra/terraform'))
$overridePath = Join-Path $infraRoot 'backend.local.hcl'
if (-not (Test-Path -LiteralPath $overridePath)) {
    throw 'Missing backend.local.hcl. Run prepare-workshop4.ps1 first.'
}

if ($Action -eq 'apply' -and -not (Test-Path -LiteralPath (Join-Path $infraRoot 'workshop4.tfplan'))) {
    throw 'Missing saved plan. Run this script with -Action plan and review the result first.'
}

$terraformCommand = Get-Command terraform.exe -ErrorAction SilentlyContinue
if ($terraformCommand) {
    $terraformExecutable = $terraformCommand.Source
} else {
    $packageRoot = Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Packages'
    $terraformCandidates = @(Get-ChildItem -LiteralPath $packageRoot -Directory -Filter 'Hashicorp.Terraform*' |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter terraform.exe -Recurse })
    if ($terraformCandidates.Count -ne 1) {
        throw 'Cannot uniquely locate Terraform. Add the installed terraform.exe directory to PATH.'
    }
    $terraformExecutable = $terraformCandidates[0].FullName
}

# Pass preconstructed strings to avoid PowerShell interpreting native arguments.
$terraformArguments = switch ($Action) {
    'init' { @('init', '-backend-config=backend.local.hcl', '-input=false', '-no-color') }
    'validate' { @('validate', '-no-color') }
    'plan' { @('plan', '-out=workshop4.tfplan', '-input=false', '-no-color') }
    'apply' { @('apply', '-input=false', '-no-color', 'workshop4.tfplan') }
}
Write-Host "Terraform: $terraformExecutable"
Write-Host "Directory: $infraRoot"
Write-Host ('Arguments: ' + ($terraformArguments -join ' '))

Push-Location -LiteralPath $infraRoot
try {
    & $terraformExecutable @terraformArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Terraform $Action failed with exit code $LASTEXITCODE. See the error above."
    }
} finally {
    Pop-Location
}
