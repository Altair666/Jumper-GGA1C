#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Commit')]
param(
    [Parameter(Position = 0, ParameterSetName = 'Commit')][string]$Message,
    [Parameter(Mandatory, ParameterSetName = 'PushOnly')][switch]$PushOnly
)

$ErrorActionPreference = 'Stop'
$gitExe = (Get-Command git.exe -CommandType Application | Select-Object -First 1).Source

function Invoke-RepoGit([string[]]$GitArgs, [int[]]$Accept = @(0), [string]$InputText) {
    # Windows PowerShell 5.1 represents native stderr as ErrorRecord. Only the exit code decides.
    $ErrorActionPreference = 'Continue'
    if ($PSBoundParameters.ContainsKey('InputText')) {
        $lines = @($InputText | & $gitExe @GitArgs 2>&1)
    } else {
        $lines = @(& $gitExe @GitArgs 2>&1)
    }
    $code = $LASTEXITCODE
    if ($code -notin $Accept) {
        $detail = ($lines | ForEach-Object { $_.ToString() }) -join ' '
        $detail = $detail -replace '(https?://)[^/\s@]+@', '$1[redacted]@'
        throw "git $($GitArgs[0]) failed ($code): $detail"
    }
    if ($code -eq 0) { $lines | ForEach-Object { $_.ToString() } }
}

$oldPrompt = $env:GIT_TERMINAL_PROMPT
$oldGcm = $env:GCM_INTERACTIVE
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$OutputEncoding = [Text.UTF8Encoding]::new($false)
$oldConsoleEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = $OutputEncoding
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $root = Invoke-RepoGit @('rev-parse', '--show-toplevel')
    if ([IO.Path]::GetFullPath($root) -ne (Get-Location).Path) { throw 'Run in the project checkout.' }
    $branch = Invoke-RepoGit @('branch', '--show-current')
    if (!$branch) { throw 'No branch is checked out (detached HEAD).' }
    foreach ($gitArgs in @('remote get-url --all origin', 'remote get-url --push --all origin')) {
        $urls = @(Invoke-RepoGit ($gitArgs -split ' '))
        if ($urls.Count -ne 1 -or $urls[0] -notmatch '^(?:https://github\.com/|git@github\.com:)Altair666/Jumper-GGA1C(?:\.git)?$') {
            throw 'origin must point only to the GGA1C GitHub repository, without embedded credentials.'
        }
    }
    foreach ($state in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-apply', 'rebase-merge', 'sequencer')) {
        if (Test-Path -LiteralPath (Invoke-RepoGit @('rev-parse', '--git-path', $state))) { throw "Finish the current Git operation ($state) first." }
    }

    if (!(Test-Path -LiteralPath '.gitignore' -PathType Leaf)) { throw '.gitignore is missing.' }
    $probes = @('src/.env', 'src/.env.local', 'probe.pem', 'probe.key', 'probe.pfx', 'probe.p12', 'id_ed25519',
        'credentials.json', 'secrets.json', '.claude/settings.local.json', 'mysql-app.txt', 'probe.log', 'probe.pid',
        'src/node_modules/probe.js', 'src/GGA1C.Sync.Blazor/Logs/logs.txt', 'test/example.ezpx')
    $ignored = @(Invoke-RepoGit (@('check-ignore', '-v', '--no-index', '--') + $probes) @(0, 1))
    if (@($ignored | Where-Object { $_ -match '^\.gitignore:' }).Count -ne $probes.Count) {
        throw '.gitignore does not protect all required paths; no files staged.'
    }
    if (@(Invoke-RepoGit @('ls-files', '-ci', '--exclude-standard')).Count) {
        throw 'The index contains tracked ignored paths. Review them before committing or pushing.'
    }

    $dirty = @(Invoke-RepoGit @('status', '--porcelain=v1', '--untracked-files=normal'))
    if (!$PushOnly -and !$dirty.Count) { Write-Output "[$branch] Nothing to commit."; return }
    if ($PushOnly -and $dirty.Count) { throw '-PushOnly requires a clean worktree.' }

    # Fail before staging when the target branch has diverged. Never use a force push.
    $remoteRef = "refs/heads/$branch"
    $remote = Invoke-RepoGit @('ls-remote', '--exit-code', 'origin', $remoteRef) @(0, 2)
    if ($remote) {
        $remoteSha = ($remote -split '\s+')[0]
        if (!(Invoke-RepoGit @('rev-list', '--max-count=1', $remoteSha))) { throw 'Fetch the remote branch before pushing.' }
        $relative = Invoke-RepoGit @('rev-list', '--count', "HEAD..$remoteSha")
        if ($relative -ne '0') { throw 'The remote branch is ahead or divergent. No files staged.' }
    }
    if (!$PushOnly) {
        if ([string]::IsNullOrWhiteSpace($Message)) { $Message = 'Update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') }
        $default = Invoke-RepoGit @('symbolic-ref', '--quiet', 'refs/remotes/origin/HEAD') @(0, 1)
        $scratch = $null
        if ($branch -eq 'main' -or $default -eq "refs/remotes/origin/$branch") {
            $scratch = 'automation/push-' + [guid]::NewGuid().ToString('N')
            $null = Invoke-RepoGit @('switch', '-c', $scratch)
        }
        try {
            $null = Invoke-RepoGit @('add', '-A', '--', '.')
            if (!(Invoke-RepoGit @('diff', '--cached', '--name-only'))) { Write-Output "[$branch] Nothing to commit."; return }
            # Keep normal hooks and signing. Use UTF-8 stdin for messages with quotes or non-ASCII text.
            $null = Invoke-RepoGit @('diff', '--cached', '--check')
            $null = Invoke-RepoGit @('commit', '--file=-') -InputText $Message
        } finally {
            if ($scratch) {
                $null = Invoke-RepoGit @('switch', $branch)
                $null = Invoke-RepoGit @('merge', '--ff-only', $scratch)
                $null = Invoke-RepoGit @('branch', '-d', $scratch)
            }
        }
    }
    $sha = Invoke-RepoGit @('rev-parse', 'HEAD')
    $subject = Invoke-RepoGit @('log', '-1', '--format=%s')
    $null = Invoke-RepoGit @('push', 'origin', "HEAD:$remoteRef")
    $published = Invoke-RepoGit @('ls-remote', '--exit-code', 'origin', $remoteRef)
    if (($published -split '\s+')[0] -ne $sha) { throw 'GitHub branch does not point to the local commit after push.' }
    Write-Output "[$branch] $($sha.Substring(0, 7)) $subject"
} catch {
    Write-Error -ErrorAction Continue $_.Exception.Message
    exit 1
} finally {
    Pop-Location
    $env:GIT_TERMINAL_PROMPT = $oldPrompt
    $env:GCM_INTERACTIVE = $oldGcm
    [Console]::OutputEncoding = $oldConsoleEncoding
}
