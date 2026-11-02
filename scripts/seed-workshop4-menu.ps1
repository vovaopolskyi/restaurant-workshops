param([switch]$Apply)

$ErrorActionPreference = 'Stop'
$env:AWS_PAGER = ''
$workshopRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dataset = Get-Content -LiteralPath (Join-Path $workshopRoot 'demo-menu.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

function ConvertTo-DynamoAttribute($Value) {
    if ($Value -is [string]) { return @{ S = $Value } }
    if ($Value -is [System.Array]) {
        $entries = @($Value | ForEach-Object { ConvertTo-DynamoAttribute $_ })
        return @{ L = $entries }
    }
    if ($Value -is [System.ValueType] -and $Value -isnot [bool]) {
        return @{ N = [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture) }
    }
    throw 'Unsupported demo attribute type.'
}

$preparedItems = @()
foreach ($tableName in @('Locations', 'Dishes')) {
    foreach ($record in $dataset.$tableName) {
        $item = [ordered]@{}
        foreach ($property in $record.PSObject.Properties) {
            $item[$property.Name] = ConvertTo-DynamoAttribute $property.Value
        }
        $preparedItems += @{ Table = $tableName; Id = $record.id; Item = $item }
    }
}

if (-not $Apply) {
    foreach ($prepared in $preparedItems) {
        Write-Output ($prepared | ConvertTo-Json -Depth 20 -Compress)
    }
    Write-Output 'Preview only. No AWS requests made. Use -Apply to insert these demo records.'
    exit 0
}

$account = & aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne '054776129200') {
    throw 'Cannot verify the expected AWS account.'
}

$diagnosticsRoot = Join-Path $workshopRoot 'diagnostics'
New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
$requestPath = Join-Path $diagnosticsRoot 'demo-menu-item.json'
foreach ($prepared in $preparedItems) {
    $payload = $prepared.Item | ConvertTo-Json -Depth 20 -Compress
    [System.IO.File]::WriteAllText($requestPath, $payload, $utf8WithoutBom)
    $arguments = @('dynamodb', 'put-item', '--table-name', $prepared.Table,
        '--item', "file://$requestPath", '--condition-expression', 'attribute_not_exists(id)',
        '--region', 'us-west-2', '--output', 'json')
    $ErrorActionPreference = 'Continue'
    try {
        $commandOutput = & aws @arguments 2>&1
        $commandExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = 'Stop'
    }
    if ($commandExitCode -eq 0) {
        Write-Host ("Added $($prepared.Table): $($prepared.Id)")
    } elseif (($commandOutput | Out-String) -match 'ConditionalCheckFailedException') {
        Write-Host ("Already exists, skipped: $($prepared.Id)")
    } else {
        throw ($commandOutput | Out-String)
    }
}
Write-Host 'Demo menu data inserted. Refresh the Menu page. These are sample records; no restaurant tables were created.'
