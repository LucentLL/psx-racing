# The shared sandbox lock, taken ATOMICALLY enough for agents that poll it
# (2026-11-16: two jobs that both saw <sandbox>\AGENT_LOCK.txt free a second
# apart both wrote it, and one mirrored its code over the other's running job).
#
#   . "$PSScriptRoot\sandbox-lock.ps1"
#   $stamp = Enter-SandboxLock -Project $proj -Me "camcorder"
#   try { ... } finally { Exit-SandboxLock -Project $proj -Stamp $stamp }
#
# Enter: a random pause, wait while a LIVE lock stands (someone else's, under
# 90 minutes old - a live lock is never taken), write "<me> <time> <unique>",
# wait 3 s, read it back; if it is not this stamp, somebody else won: go round
# again. Exit: delete the lock only if it is still this stamp.
function Enter-SandboxLock {
    param([Parameter(Mandatory = $true)][string]$Project, [Parameter(Mandatory = $true)][string]$Me, [int]$StaleMinutes = 90)
    $lock = Join-Path $Project "AGENT_LOCK.txt"
    $stamp = "$Me $(Get-Date -Format s) $([guid]::NewGuid().ToString('N').Substring(0, 8))"
    while ($true) {
        Start-Sleep -Milliseconds (Get-Random -Minimum 200 -Maximum 4000)
        $waited = $false
        while (Test-Path $lock) {
            $txt = ""
            try { $txt = (Get-Content $lock -Raw -ErrorAction Stop).Trim() } catch { }
            $age = 0
            try { $age = ((Get-Date) - (Get-Item $lock -ErrorAction Stop).LastWriteTime).TotalMinutes } catch { break }
            if ($age -gt $StaleMinutes) { Write-Host "the lock '$txt' is $([int]$age) min old (stale): taking it"; break }
            if (-not $waited) { Write-Host "sandbox locked by '$txt' ($([int]$age) min) - waiting"; $waited = $true }
            Start-Sleep -Seconds 20
        }
        $stamp = "$Me $(Get-Date -Format s) $([guid]::NewGuid().ToString('N').Substring(0, 8))"
        $stamp | Out-File -FilePath $lock -Encoding ascii
        Start-Sleep -Seconds 3
        $back = ""
        try { $back = (Get-Content $lock -Raw -ErrorAction Stop).Trim() } catch { }
        if ($back -eq $stamp) { Write-Host "LOCK taken: $stamp"; return $stamp }
        Write-Host "lost the lock to '$back' - waiting again"
    }
}

function Exit-SandboxLock {
    param([Parameter(Mandatory = $true)][string]$Project, [Parameter(Mandatory = $true)][string]$Stamp)
    $lock = Join-Path $Project "AGENT_LOCK.txt"
    if (Test-Path $lock) {
        $txt = ""
        try { $txt = (Get-Content $lock -Raw -ErrorAction Stop).Trim() } catch { }
        if ($txt -eq $Stamp) { Remove-Item $lock -Force; Write-Host "LOCK released: $Stamp" }
        else { Write-Host "the lock is no longer ours ('$txt'): left alone" }
    }
}
