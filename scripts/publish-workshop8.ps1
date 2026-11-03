$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$branch = 'feature/workshop-08-e2e'
Push-Location -LiteralPath $repositoryRoot
try {
    $currentBranch = & git branch --show-current
    if ($LASTEXITCODE -ne 0 -or $currentBranch.Trim() -ne $branch) { throw "Switch to $branch before publishing." }
    $changes = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit or review pending changes before publishing.' }
    $localCommit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the local commit.' }
    & git push -u origin $branch
    if ($LASTEXITCODE -ne 0) { throw 'Git push failed. Keep the error output for diagnosis.' }
    $remoteRef = & git ls-remote --exit-code origin "refs/heads/$branch"
    if ($LASTEXITCODE -ne 0 -or -not $remoteRef -or $remoteRef.Split()[0] -ne $localCommit.Trim()) {
        throw 'Push returned, but the remote commit could not be verified.'
    }
    Write-Host 'Workshop 8 branch published and its latest commit verified.'
    Write-Host "https://github.com/vovaopolskyi/restaurant-workshops/tree/$branch"
    Write-Host 'README includes the test results and steps to enable live GitHub Actions runs.'
    Write-Host 'Private credentials and local reports were not included in the commit.'
} finally { Pop-Location }
