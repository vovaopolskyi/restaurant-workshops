param([Parameter(Mandatory = $true)][ValidatePattern('^\d{12}$')][string]$ExpectedAccountId)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$envPath = Join-Path $repoRoot 'aqa/restaurant-aqa-framework/.env'
$manifestPath = Join-Path $repoRoot 'diagnostics/workshop8/bootstrap.json'
$utf8 = New-Object Text.UTF8Encoding($false)
$lines = [IO.File]::ReadAllLines($envPath)
$settings = @{}
foreach ($line in $lines) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$matches[1]] = $matches[2].Trim().Trim([char]39).Trim([char]34) }
}
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ($manifest.accountId -ne $ExpectedAccountId -or $settings.AWS_ACCOUNT_ID -ne $ExpectedAccountId) { throw 'Fixture account mismatch.' }
if ($manifest.userPoolId -ne $settings.AWS_USER_POOL_ID) { throw 'Fixture user pool mismatch.' }
$privatePath = Join-Path $repoRoot ('diagnostics/workshop8/repair-' + [Guid]::NewGuid().ToString('N') + '.json')

function Invoke-RepairAws([string]$Service, [string]$Operation, [hashtable]$InputData) {
    # Restrict the temporary request before writing any password into it.
    [IO.File]::WriteAllText($privatePath, '', $utf8)
    $acl = Get-Acl -LiteralPath $privatePath
    $acl.SetAccessRuleProtection($true, $false)
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($currentUser, 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $privatePath -AclObject $acl
    [IO.File]::WriteAllText($privatePath, ($InputData | ConvertTo-Json -Depth 40 -Compress), $utf8)
    try {
        $ErrorActionPreference = 'Continue'
        $result = & aws $Service $Operation --cli-input-json "file://$privatePath" --region $settings.AWS_REGION --output json --no-cli-pager 2>&1
        $code = $LASTEXITCODE
        $text = $result | Out-String
        if ($code -ne 0) {
            foreach ($kind in @('OWNER', 'OTHER', 'WAITER')) { $text = $text.Replace($settings["${kind}_PASSWORD"], '[redacted]') }
            throw "AWS $Operation failed: $text"
        }
        if ($text.Trim()) { return $text | ConvertFrom-Json }
    } finally { Remove-Item -LiteralPath $privatePath -Force -ErrorAction SilentlyContinue }
}

function Save-Configuration {
    $updated = foreach ($line in $lines) {
        if ($line -match '^(OWNER|OTHER|WAITER)_EMAIL=') {
            $key = $line.Split('=')[0]
            "$key=$($settings[$key])"
        } else { $line }
    }
    [IO.File]::WriteAllText($envPath, (($updated -join "`n") + "`n"), $utf8)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 40), $utf8)
}

$identity = Invoke-RepairAws 'sts' 'get-caller-identity' @{}
if ($identity.Account -ne $ExpectedAccountId) { throw 'Active AWS account mismatch. No accounts were changed.' }
foreach ($kind in @('OWNER', 'OTHER', 'WAITER')) {
    $oldUsername = $settings["${kind}_EMAIL"]
    if ($oldUsername -notmatch '@example\.invalid$') { continue }
    $entry = @($manifest.users | Where-Object { $_.kind -eq $kind -and $_.username -eq $oldUsername })
    if ($entry.Count -ne 1 -or $oldUsername -ne "aqa-$($kind.ToLowerInvariant())-$($manifest.runId)@example.invalid") {
        throw 'Refusing to replace an account outside the recorded workshop fixtures.'
    }
    $entry = $entry[0]
    $username = $oldUsername.Replace('@example.invalid', '@example.com')
    $password = $settings["${kind}_PASSWORD"]
    $role = if ($kind -eq 'WAITER') { 'Waiter' } else { 'Customer' }
    $existing = Invoke-RepairAws 'cognito-idp' 'list-users' @{ UserPoolId = $manifest.userPoolId; Filter = "username = `"$username`"" }
    if (@($existing.Users).Count -eq 0) {
        Invoke-RepairAws 'cognito-idp' 'admin-create-user' @{ UserPoolId = $manifest.userPoolId; Username = $username;
            TemporaryPassword = $password; MessageAction = 'SUPPRESS'; UserAttributes = @(
                @{ Name = 'email'; Value = $username }, @{ Name = 'email_verified'; Value = 'true' },
                @{ Name = 'given_name'; Value = 'Aqa' }, @{ Name = 'family_name'; Value = $kind }) } | Out-Null
    }
    Invoke-RepairAws 'cognito-idp' 'admin-set-user-password' @{ UserPoolId = $manifest.userPoolId; Username = $username; Password = $password; Permanent = $true } | Out-Null
    Invoke-RepairAws 'cognito-idp' 'admin-add-user-to-group' @{ UserPoolId = $manifest.userPoolId; Username = $username; GroupName = $role } | Out-Null
    $user = Invoke-RepairAws 'cognito-idp' 'admin-get-user' @{ UserPoolId = $manifest.userPoolId; Username = $username }
    $newId = ($user.UserAttributes | Where-Object { $_.Name -eq 'sub' }).Value
    if (-not $newId) { throw 'Replacement account has no user identifier.' }
    if ($kind -eq 'WAITER') {
        $oldKey = @{ userId = @{ S = $entry.sub } }
        $oldWaiter = (Invoke-RepairAws 'dynamodb' 'get-item' @{ TableName = 'Waiters'; Key = $oldKey; ConsistentRead = $true }).Item
        if (-not $oldWaiter -or $oldWaiter.locationId.S -ne $settings.TEST_LOCATION_ID -or @($oldWaiter.reservations.L).Count -gt 0) {
            throw 'The original test waiter is missing, belongs to another location or already has reservations. Automatic replacement stopped.'
        }
        $oldWaiter.userId = @{ S = $newId }
        Invoke-RepairAws 'dynamodb' 'put-item' @{ TableName = 'Waiters'; Item = $oldWaiter;
            ConditionExpression = 'attribute_not_exists(userId) OR locationId = :location';
            ExpressionAttributeValues = @{ ':location' = @{ S = $settings.TEST_LOCATION_ID } } } | Out-Null
        Invoke-RepairAws 'dynamodb' 'delete-item' @{ TableName = 'Waiters'; Key = $oldKey;
            ConditionExpression = 'locationId = :location'; ExpressionAttributeValues = @{ ':location' = @{ S = $settings.TEST_LOCATION_ID } } } | Out-Null
        foreach ($item in $manifest.dynamoItems) {
            if ($item.tableName -eq 'Waiters' -and $item.key.userId.S -eq $entry.sub) { $item.key.userId.S = $newId }
        }
    }
    # Disable the exact original fixture account before recording its replacement.
    Invoke-RepairAws 'cognito-idp' 'admin-disable-user' @{ UserPoolId = $manifest.userPoolId; Username = $oldUsername } | Out-Null
    $entry | Add-Member -NotePropertyName previousUsername -NotePropertyValue $oldUsername -Force
    $entry.username = $username
    $entry.sub = $newId
    $settings["${kind}_EMAIL"] = $username
    Save-Configuration
    Write-Host "Repaired $kind test account. Credentials remain in the private .env."
}
Write-Host 'Workshop 8 account compatibility check complete.'
