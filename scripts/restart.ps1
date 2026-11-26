#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$taskName = 'GGA1C-Hub-Autostart'
$launcher = 'C:\GGA1C\start-hub.ps1'
$logPath = 'C:\GGA1C\hub-run.log'
$pidPath = 'C:\GGA1C\hub.pid'
$appDir = Join-Path $repo 'src\GGA1C.Sync.Blazor'
$exe = Join-Path $appDir 'bin\Release\net10.0\GGA1C.Sync.Blazor.exe'
$launcherArg = '(?:^|\s)-File\s+"?' + [regex]::Escape($launcher) + '"?\s*$'
$dotnetArgs = '\brun\s+--no-launch-profile\s+-c\s+Release\s+--urls\s+"?https://0\.0\.0\.0:5443;http://0\.0\.0\.0:5080'
$curlExe = (Get-Command curl.exe -CommandType Application | Select-Object -First 1).Source

function Get-HubListeners {
    Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in @(5443, 5080) }
}

function Get-LauncherProcesses {
    Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
        Where-Object { $_.CommandLine -match $launcherArg }
}

function Get-HubRunners($LaunchProcess) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId = $($LaunchProcess.ProcessId)" |
        Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match $dotnetArgs }
}

function Get-HubApps($RunnerProcess) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId = $($RunnerProcess.ProcessId)" |
        Where-Object { $_.ExecutablePath -eq $exe }
}

function Get-SameProcess($Previous) {
    Get-CimInstance Win32_Process -Filter "ProcessId = $($Previous.ProcessId)" |
        Where-Object { $_.CreationDate -eq $Previous.CreationDate -and $_.ExecutablePath -eq $Previous.ExecutablePath }
}

$mutex = [Threading.Mutex]::new($false, 'Global\GGA1C.Hub.Restart')
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'Another hub restart is already running.' }
    if ($repo -ne 'C:\gg1ac') { throw 'Use the live checkout C:\gg1ac, not a worktree or another copy.' }
    $task = Get-ScheduledTask -TaskName $taskName -TaskPath '\'
    if ($task.State -notin @('Running', 'Ready') -or !$task.Settings.AllowDemandStart -or
        $task.Actions.Count -ne 1 -or $task.Actions[0].Execute -ne 'powershell.exe' -or
        $task.Actions[0].Arguments -notmatch $launcherArg) { throw 'Unexpected autostart task configuration.' }
    $scriptText = [IO.File]::ReadAllText($launcher)
    $changeDir = '(?m)^\s*Set-Location\s+"?' + [regex]::Escape($appDir) + '"?\s*$'
    $logOutput = '\*>\s+"?' + [regex]::Escape($logPath) + '"?\s*$'
    if ($scriptText -notmatch $changeDir -or $scriptText -notmatch $dotnetArgs -or $scriptText -notmatch $logOutput) {
        throw 'The launcher no longer matches this checkout, port pair, or log path.'
    }
    if ((Test-Path -LiteralPath $pidPath) -and
        ((Get-Item -LiteralPath $pidPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to write through a redirected PID file.'
    }

    $launches = @(Get-LauncherProcesses)
    if ($launches.Count -gt 1 -or ($task.State -eq 'Running' -and !$launches.Count)) {
        throw 'Cannot identify a unique running hub launcher.'
    }
    $runners = @(foreach ($launch in $launches) { Get-HubRunners $launch })
    $apps = @(foreach ($runner in $runners) { Get-HubApps $runner })
    $listeners = @(Get-HubListeners)
    if ($runners.Count -gt 1 -or $apps.Count -gt 1 -or
        @($listeners | Where-Object { $_.OwningProcess -notin $apps.ProcessId }).Count) {
        throw 'Unexpected process on the hub ports. No process was stopped.'
    }
    $old = @($apps + $runners + $launches)
    if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName -TaskPath '\' }
    if ($old.Count) {
        # Give Task Scheduler a chance to stop the launcher; clean up only verified survivors.
        $until = (Get-Date).AddSeconds(5)
        do {
            $survivors = @(foreach ($process in $old) { Get-SameProcess $process })
            if (!$survivors.Count) { break }
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $until)
        foreach ($process in $old) {
            if (Get-SameProcess $process) { Stop-Process -Id $process.ProcessId -Force }
        }
    }
    if (@(Get-HubListeners).Count -or (Get-ScheduledTask -TaskName $taskName -TaskPath '\').State -ne 'Ready') {
        throw 'The previous instance has not stopped; no second instance was started.'
    }
    $startedAt = Get-Date
    Start-ScheduledTask -TaskName $taskName -TaskPath '\'
    $deadline = $startedAt.AddSeconds(150)
    do {
        $newListeners = @(Get-HubListeners)
        $owners = @($newListeners.OwningProcess | Select-Object -Unique)
        if (@($newListeners.LocalPort | Select-Object -Unique).Count -eq 2 -and $owners.Count -eq 1) {
            $newApp = Get-CimInstance Win32_Process -Filter "ProcessId = $($owners[0])"
            if (!$newApp -or $newApp.ExecutablePath -ne $exe -or $newApp.CreationDate -lt $startedAt) {
                throw 'The hub ports are not served by the new Release process.'
            }
            $status = & $curlExe --noproxy '*' --insecure --silent --max-time 3 --output NUL --write-out '%{http_code}' 'https://localhost:5443/Account/Login'
            if ($LASTEXITCODE -eq 0 -and $status -eq '200') {
                if (!(Test-Path -LiteralPath $logPath -PathType Leaf)) { throw 'The hub log was not created.' }
                [IO.File]::WriteAllText($pidPath, "$($newApp.ProcessId)`r`n", [Text.Encoding]::ASCII)
                Write-Output "PID=$($newApp.ProcessId) | HTTP 200 | log=$logPath"
                return
            }
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Hub startup did not reach HTTP 200 within 150 seconds. Check $logPath."
} catch {
    Write-Error -ErrorAction Continue $_.Exception.Message
    exit 1
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
