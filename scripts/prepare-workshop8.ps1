param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{12}$')]
    [string]$ExpectedAccountId,
    [string]$Region = 'us-west-2',
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl
)

$ErrorActionPreference = 'Stop'
$env:AWS_PAGER = ''
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frameworkRoot = Join-Path $repoRoot 'aqa/restaurant-aqa-framework'
$envPath = Join-Path $frameworkRoot '.env'
$diagnosticsRoot = Join-Path $repoRoot 'diagnostics/workshop8'
$manifestPath = Join-Path $diagnosticsRoot 'bootstrap.json'
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
$secretValues = New-Object 'System.Collections.Generic.List[string]'
$manifest = $null

if (Test-Path -LiteralPath $envPath) { throw 'The AQA .env already exists. It was not changed; use the existing configuration or review it manually.' }
if (Test-Path -LiteralPath $manifestPath) { throw 'A bootstrap manifest already exists. Review its resources/cleanup status before starting another bootstrap.' }
if (-not (Test-Path -LiteralPath $frameworkRoot)) { throw 'AQA framework directory is missing.' }
foreach ($target in @($BaseUrl, $ApiBaseUrl)) {
    $uri = $null
    if (-not [Uri]::TryCreate($target, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https') -or $uri.UserInfo -or $target -match '[\r\n]') {
        throw 'Supply absolute HTTP(S) URLs without embedded credentials or line breaks.'
    }
}
Get-Command aws -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
$privateRoot = Join-Path $diagnosticsRoot ('private-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $privateRoot | Out-Null

function Protect-LocalPath([string]$Path, [switch]$Directory) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [Security.AccessControl.InheritanceFlags]::None
    if ($Directory) { $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit' }
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', $inheritance, 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Write-Manifest {
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 30), $utf8WithoutBom)
}

function Invoke-AwsJson([string]$Service, [string]$Operation, [hashtable]$InputData = @{}) {
    $requestPath = Join-Path $privateRoot ([Guid]::NewGuid().ToString('N') + '.json')
    [IO.File]::WriteAllText($requestPath, ($InputData | ConvertTo-Json -Depth 30 -Compress), $utf8WithoutBom)
    try {
        # Sensitive values are read from a private temporary file, never process arguments.
        $awsArguments = @($Service, $Operation, '--cli-input-json', "file://$requestPath", '--region', $Region,
            '--output', 'json', '--no-cli-pager', '--no-cli-auto-prompt')
        $ErrorActionPreference = 'Continue'
        $responseLines = & aws @awsArguments 2>&1
        $exitCode = $LASTEXITCODE
        $responseText = $responseLines | Out-String
        if ($exitCode -ne 0) {
            foreach ($secretValue in $secretValues) { $responseText = $responseText.Replace($secretValue, '[redacted]') }
            throw "AWS $Service $Operation failed: $responseText"
        }
        if (-not [string]::IsNullOrWhiteSpace($responseText)) { return $responseText | ConvertFrom-Json }
    } finally {
        Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue
    }
}

function New-TestPassword {
    $alphabets = @('ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@%+-_')
    $allCharacters = $alphabets -join ''
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $characters = New-Object char[] 16
    for ($index = 0; $index -lt 16; $index++) {
        $alphabet = $allCharacters
        if ($index -lt 4) { $alphabet = $alphabets[$index] }
        $characters[$index] = $alphabet[$bytes[$index] % $alphabet.Length]
    }
    for ($index = 15; $index -gt 0; $index--) {
        $swap = $bytes[31 - $index] % ($index + 1)
        $temporary = $characters[$index]; $characters[$index] = $characters[$swap]; $characters[$swap] = $temporary
    }
    return -join $characters
}

try {
    Protect-LocalPath $privateRoot -Directory
    $identity = Invoke-AwsJson 'sts' 'get-caller-identity'
    if ($identity.Account -ne $ExpectedAccountId) { throw 'The active AWS account does not match ExpectedAccountId. No cloud resources were changed.' }

    $pools = @((Invoke-AwsJson 'cognito-idp' 'list-user-pools' @{ MaxResults = 60 }).UserPools |
        Where-Object { $_.Name -eq 'restaurant-backend-user-pool' })
    if ($pools.Count -ne 1) { throw 'Expected exactly one restaurant-backend-user-pool in the selected region.' }
    $userPoolId = $pools[0].Id
    $catalog = Invoke-AwsJson 'dynamodb' 'scan' @{ TableName = 'Dishes'; ConsistentRead = $true }
    $available = @($catalog.Items | Where-Object { $_.state.N -eq '1' -and $_.id.S } | Select-Object -First 2)
    if ($available.Count -lt 2) { throw 'At least two available dishes must exist before preparing AQA fixtures.' }
    $locations = Invoke-AwsJson 'dynamodb' 'scan' @{ TableName = 'Locations'; ConsistentRead = $true; Limit = 1 }
    if (@($locations.Items).Count -eq 0) { throw 'At least one existing location is required as the fixture template.' }

    $runId = [Guid]::NewGuid().ToString('N').Substring(0, 16)
    $locationId = "aqa-location-$runId"
    $tableIds = @(1..8 | ForEach-Object { "aqa-table-$runId-$_" })
    $manifest = [ordered]@{ version = 1; status = 'started'; accountId = $ExpectedAccountId; region = $Region;
        userPoolId = $userPoolId; runId = $runId; createdAtUtc = [DateTime]::UtcNow.ToString('o');
        users = @(); groupsCreated = @(); dynamoItems = @(); envPath = $envPath }
    Write-Manifest
    $groups = @((Invoke-AwsJson 'cognito-idp' 'list-groups' @{ UserPoolId = $userPoolId }).Groups | ForEach-Object { $_.GroupName })
    foreach ($role in @('Customer', 'Waiter')) {
        if ($groups -notcontains $role) {
            Invoke-AwsJson 'cognito-idp' 'create-group' @{ UserPoolId = $userPoolId; GroupName = $role } | Out-Null
            $manifest.groupsCreated += $role
            Write-Manifest
        }
    }

    $accounts = @{}
    foreach ($kind in @('OWNER', 'OTHER', 'WAITER')) {
        # The application's legacy email regex accepts only 2-3 character TLDs.
        # example.com is reserved for examples; invitation delivery is suppressed.
        $username = "aqa-$($kind.ToLowerInvariant())-$runId@example.com"
        $password = New-TestPassword
        $secretValues.Add($password)
        $role = 'Customer'
        if ($kind -eq 'WAITER') { $role = 'Waiter' }
        $userManifest = [ordered]@{ username = $username; kind = $kind; role = $role; state = 'planned'; sub = $null }
        $manifest.users += $userManifest
        Write-Manifest
        $created = Invoke-AwsJson 'cognito-idp' 'admin-create-user' @{ UserPoolId = $userPoolId; Username = $username;
            TemporaryPassword = $password; MessageAction = 'SUPPRESS';
            UserAttributes = @(@{ Name = 'email'; Value = $username }, @{ Name = 'email_verified'; Value = 'true' },
                @{ Name = 'given_name'; Value = 'Aqa' }, @{ Name = 'family_name'; Value = $kind }) }
        $userManifest.state = 'created'
        $userManifest.sub = ($created.User.Attributes | Where-Object { $_.Name -eq 'sub' } | Select-Object -First 1).Value
        Write-Manifest
        if (-not $userManifest.sub) { throw 'Cognito did not return the new fixture user identifier.' }
        Invoke-AwsJson 'cognito-idp' 'admin-set-user-password' @{ UserPoolId = $userPoolId; Username = $username;
            Password = $password; Permanent = $true } | Out-Null
        Invoke-AwsJson 'cognito-idp' 'admin-add-user-to-group' @{ UserPoolId = $userPoolId; Username = $username; GroupName = $role } | Out-Null
        $userManifest.state = 'ready'
        $accounts[$kind] = @{ Username = $username; Password = $password; Sub = $userManifest.sub }
        Write-Manifest
    }

    $location = $locations.Items[0]
    $overrides = @{ id = @{ S = $locationId }; address = @{ S = 'AQA isolated test restaurant' };
        description = @{ S = 'Dedicated workshop 8 test fixtures.' }; totalCapacity = @{ N = '48' };
        averageOccupancy = @{ S = '0%' }; specialtyDishIds = @{ L = @($available | ForEach-Object { @{ S = $_.id.S } }) } }
    foreach ($propertyName in $overrides.Keys) { $location | Add-Member -NotePropertyName $propertyName -NotePropertyValue $overrides[$propertyName] -Force }
    $items = @(@{ TableName = 'Locations'; KeyName = 'id'; KeyValue = $locationId; Item = $location })
    for ($number = 1; $number -le 8; $number++) {
        $tableId = $tableIds[$number - 1]
        $items += @{ TableName = 'Tables'; KeyName = 'id'; KeyValue = $tableId;
            Item = @{ id = @{ S = $tableId }; locationId = @{ S = $locationId }; capacity = @{ N = '6' }; tableNumber = @{ N = [string]$number } } }
    }
    $waiterId = $accounts['WAITER'].Sub
    $items += @{ TableName = 'Waiters'; KeyName = 'userId'; KeyValue = $waiterId;
        Item = @{ userId = @{ S = $waiterId }; locationId = @{ S = $locationId };
            createdAt = @{ S = [DateTime]::UtcNow.ToString('o') }; reservations = @{ L = @() } } }
    foreach ($item in $items) {
        $entry = [ordered]@{ tableName = $item.TableName; key = @{ $item.KeyName = @{ S = $item.KeyValue } }; state = 'planned' }
        $manifest.dynamoItems += $entry
        Write-Manifest
        Invoke-AwsJson 'dynamodb' 'put-item' @{ TableName = $item.TableName; Item = $item.Item;
            ConditionExpression = "attribute_not_exists($($item.KeyName))" } | Out-Null
        $entry.state = 'created'
        Write-Manifest
    }

    # Recheck before the exclusive create: a second process must not replace an existing configuration.
    $lines = @('# Generated private workshop 8 fixtures. Do not commit this file.', 'RUN_LIVE_E2E=true',
        "BASE_URL=$($BaseUrl.TrimEnd('/'))", "API_BASE_URL=$($ApiBaseUrl.TrimEnd('/'))")
    foreach ($kind in @('OWNER', 'OTHER', 'WAITER')) {
        $lines += "$($kind)_EMAIL=$($accounts[$kind].Username)"
        $lines += "$($kind)_PASSWORD='$($accounts[$kind].Password)'"
    }
    $lines += @("TEST_TABLE_ID=$($tableIds[0])", "TEST_TABLE_IDS=$($tableIds -join ',')", "TEST_LOCATION_ID=$locationId",
        "AWS_ACCOUNT_ID=$ExpectedAccountId", "AWS_REGION=$Region", "AWS_USER_POOL_ID=$userPoolId",
        'ENABLE_ADMIN_FIXTURES=true', 'AWS_GUEST_SESSIONS_TABLE=GuestSessions')
    $stream = [IO.File]::Open($envPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $stream.Dispose()
    Protect-LocalPath $envPath
    [IO.File]::WriteAllText($envPath, (($lines -join "`n") + "`n"), $utf8WithoutBom)
    $manifest.status = 'complete'
    Write-Manifest
    Write-Host "Private test configuration saved: $envPath"
    Write-Host "Fixture manifest saved: $manifestPath"
} catch {
    if ($null -ne $manifest) { $manifest.status = 'failed'; Write-Manifest }
    $message = $_.Exception.Message
    foreach ($secretValue in $secretValues) { $message = $message.Replace($secretValue, '[redacted]') }
    throw $message
} finally {
    # Remove only the checked, per-run private directory inside the ignored diagnostics root.
    $resolvedPrivate = [IO.Path]::GetFullPath($privateRoot)
    $allowedRoot = [IO.Path]::GetFullPath($diagnosticsRoot).TrimEnd('\') + '\'
    if ($resolvedPrivate.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedPrivate)) {
        Remove-Item -LiteralPath $resolvedPrivate -Recurse -Force
    }
}
