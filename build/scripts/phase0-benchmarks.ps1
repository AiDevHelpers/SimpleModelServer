#requires -Version 7.0
<#
.SYNOPSIS
    Runs the Phase 0 benchmarks from docs/staged-pipeline-test-plan.md (Part A) against a local
    llama-server instance on the 1660 box, and appends results to docs/benchmarks/phase0-results.md.

.DESCRIPTION
    Implements A1 (VRAM headroom), A2 (throughput baseline), A3 (speculative decode payoff),
    and A4 (router latency + schema validity). A5 (classification accuracy) is NOT included here
    because it depends on the Part B golden set, which does not exist yet.

    Requires: llama-server.exe (llama.cpp) on PATH or passed via -LlamaServerPath, nvidia-smi on
    PATH, and local GGUF weights for the 7B codegen model, the 0.5B draft model, and the 1.5B
    router model.

.PARAMETER Test
    Which benchmark(s) to run: All, A1, A2, A3, A4.

.PARAMETER LlamaServerPath
    Path to llama-server executable. Defaults to "llama-server" (must be on PATH).

.PARAMETER ModelPath
    Path to the 7B codegen GGUF (Qwen2.5-Coder-7B Q4_K_M).

.PARAMETER SimpleModelServerPath
    Path to the 0.5B draft GGUF (Qwen2.5-Coder-0.5B). Required for A3.

.PARAMETER RouterModelPath
    Path to the 1.5B router GGUF (Qwen2.5-1.5B-Instruct). Required for A4.

.PARAMETER Port
    Port llama-server listens on. Default 8080.

.PARAMETER QuickIdleSeconds
    Override the 10-minute idle VRAM measurement window in A1 for a faster smoke test
    (e.g. -QuickIdleSeconds 30). Omit for the real 10-minute measurement.

.PARAMETER OutFile
    Results file to append to. Default docs/benchmarks/phase0-results.md relative to repo root.

.EXAMPLE
    ./phase0-benchmarks.ps1 -Test All `
        -ModelPath D:\models\qwen2.5-coder-7b-q4_k_m.gguf `
        -SimpleModelServerPath D:\models\qwen2.5-coder-0.5b-q8_0.gguf `
        -RouterModelPath D:\models\qwen2.5-1.5b-instruct-q8_0.gguf

.EXAMPLE
    # Fast smoke test of the harness itself, without the real 10-minute idle window
    ./phase0-benchmarks.ps1 -Test A1 -ModelPath D:\models\qwen2.5-coder-7b-q4_k_m.gguf -QuickIdleSeconds 15
#>

[CmdletBinding()]
param(
    [ValidateSet("All", "A1", "A2", "A3", "A4")]
    [string]$Test = "All",

    [string]$LlamaServerPath = "llama-server",
    [string]$ModelPath,
    [string]$SimpleModelServerPath,
    [string]$RouterModelPath,
    [int]$Port = 8080,
    [int]$QuickIdleSeconds = 0,
    [string]$OutFile = "$PSScriptRoot/../../docs/benchmarks/phase0-results.md",
    [string]$PromptsDir = "$PSScriptRoot/../../tests/prompts"
)

$ErrorActionPreference = "Stop"
$BaseUrl = "http://127.0.0.1:$Port"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Get-VramSampleMB {
    # Returns @{ UsedMB = int; TotalMB = int } for GPU 0.
    $line = (nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader,nounits) | Select-Object -First 1
    $parts = $line -split ",\s*"
    [pscustomobject]@{
        UsedMB  = [int]$parts[0]
        TotalMB = [int]$parts[1]
    }
}

function Start-VramMonitor {
    # Launches a background job sampling VRAM every 500ms until stopped.
    # Args passed as an array (not one string) to Start-Job's child process — passing them as a
    # single space-joined string caused nvidia-smi to misparse "--format=csv,noheader,nounits"
    # inside the job (but not outside one); this form works in both contexts.
    # Each sample is written to the job's output stream directly (not accumulated in a local
    # variable) so Receive-Job can retrieve them — a local $samples array is invisible outside
    # the job and Receive-Job would always return nothing.
    return Start-Job -ScriptBlock {
        while ($true) {
            (& nvidia-smi "--query-gpu=memory.used" "--format=csv,noheader,nounits") | Select-Object -First 1 | ForEach-Object { [int]$_ }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Stop-VramMonitor {
    param($Job)
    Stop-Job $Job -ErrorAction SilentlyContinue | Out-Null
    $raw = Receive-Job $Job -ErrorAction SilentlyContinue
    Remove-Job $Job -Force -ErrorAction SilentlyContinue | Out-Null
    if (-not $raw) { return [pscustomobject]@{ MinMB = $null; MaxMB = $null } }
    [pscustomobject]@{
        MinMB = ($raw | Measure-Object -Minimum).Minimum
        MaxMB = ($raw | Measure-Object -Maximum).Maximum
    }
}

function Wait-ServerHealthy {
    param([int]$TimeoutSec = 120)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "$BaseUrl/health" -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { return $true }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    throw "llama-server did not become healthy within $TimeoutSec s"
}

function Start-LlamaServer {
    param([string[]]$ExtraArgs)
    $serverArgs = @("--model", $ModelPath, "--port", $Port, "--host", "127.0.0.1") + $ExtraArgs
    Write-Host "Starting llama-server: $LlamaServerPath $($serverArgs -join ' ')" -ForegroundColor Cyan
    $proc = Start-Process -FilePath $LlamaServerPath -ArgumentList $serverArgs -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$env:TEMP\llama-server.stdout.log" `
        -RedirectStandardError "$env:TEMP\llama-server.stderr.log"
    Wait-ServerHealthy | Out-Null
    return $proc
}

function Stop-LlamaServer {
    param($Proc)
    if ($Proc -and -not $Proc.HasExited) {
        Stop-Process -Id $Proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

function Invoke-Completion {
    param(
        [string]$Prompt,
        [int]$NPredict = 256,
        [double]$Temperature = 0.2,
        [hashtable]$ExtraBody = @{}
    )
    $body = @{
        prompt      = $Prompt
        n_predict   = $NPredict
        temperature = $Temperature
        cache_prompt = $false
    }
    foreach ($k in $ExtraBody.Keys) { $body[$k] = $ExtraBody[$k] }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $resp = Invoke-RestMethod -Uri "$BaseUrl/completion" -Method Post -Body ($body | ConvertTo-Json -Depth 10) `
        -ContentType "application/json" -TimeoutSec 300
    $sw.Stop()

    [pscustomobject]@{
        Content           = $resp.content
        WallClockMs       = $sw.Elapsed.TotalMilliseconds
        PromptN           = $resp.timings.prompt_n
        PromptPerSecond   = $resp.timings.prompt_per_second
        PredictedN        = $resp.timings.predicted_n
        PredictedPerSecond = $resp.timings.predicted_per_second
        DraftN            = $resp.timings.draft_n
        DraftNAccepted    = $resp.timings.draft_n_accepted
        Raw               = $resp
    }
}

function New-FillerPrompt {
    # Builds a prompt of approximately $TargetTokens tokens using a repeated, code-comment-shaped
    # filler line (rough estimate: ~1.3 tokens/word for this text). Good enough for a throughput
    # sweep where content doesn't matter, only length.
    param([int]$TargetTokens)
    $line = "// Consider the following invariant: every order line item must reference a product that exists in the catalog at the time of order placement, and its unit price is snapshotted rather than recomputed later. "
    $wordsPerLine = ($line -split "\s+").Count
    $tokensPerLine = [math]::Ceiling($wordsPerLine * 1.3)
    $linesNeeded = [math]::Max(1, [math]::Ceiling($TargetTokens / $tokensPerLine))
    $body = ($line * $linesNeeded)
    return "$body`n// Given the above, write a short C# comment (one line) restating the invariant."
}

function Format-Row {
    param([string[]]$Cells)
    "| " + ($Cells -join " | ") + " |"
}

function Add-ResultSection {
    param([string]$Markdown)
    $dir = Split-Path -Parent $OutFile
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (-not (Test-Path $OutFile)) {
        "# Phase 0 Benchmark Results`n`n> Generated by build/scripts/phase0-benchmarks.ps1. Each run appends a timestamped section.`n" | Out-File -FilePath $OutFile -Encoding utf8
    }
    Add-Content -Path $OutFile -Value $Markdown -Encoding utf8
}

function Get-RunHeader {
    param([string]$Section)
    $gitSha = try { (git -C "$PSScriptRoot/../.." rev-parse --short HEAD 2>$null) } catch { "unknown" }
    if (-not $gitSha) { $gitSha = "unknown" }
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    @"

## $Section — $ts (git $gitSha)

"@
}

# ---------------------------------------------------------------------------
# A1. VRAM headroom
# ---------------------------------------------------------------------------

function Invoke-A1 {
    if (-not $ModelPath) { throw "A1 requires -ModelPath" }
    Write-Host "== A1: VRAM headroom ==" -ForegroundColor Yellow

    $idleSeconds = if ($QuickIdleSeconds -gt 0) { $QuickIdleSeconds } else { 600 }
    Write-Host "Sampling idle desktop VRAM for $idleSeconds s (open browser + IDE first for a realistic load)..."
    $idleMin = [int]::MaxValue
    $idleEnd = (Get-Date).AddSeconds($idleSeconds)
    while ((Get-Date) -lt $idleEnd) {
        $s = Get-VramSampleMB
        if ($s.UsedMB -lt $idleMin) { $idleMin = $s.UsedMB }
        Start-Sleep -Seconds 2
    }
    $total = (Get-VramSampleMB).TotalMB
    $idleFreeMB = $total - $idleMin
    Write-Host "Min free VRAM over idle window: $idleFreeMB MB (of $total MB total)"

    $rows = @()

    foreach ($ctx in 8192, 10240) {
        Write-Host "-- ctx-size $ctx --"
        # Deliberately NOT forcing -ngl (full GPU offload): on a 6GB card, forcing every layer
        # onto the GPU when it doesn't fit causes a hard Vulkan/CUDA device-lost crash instead of
        # a graceful CPU spill. Letting llama-server auto-fit layers is both safer and a more
        # honest measurement of what this hardware can actually sustain.
        $proc = Start-LlamaServer -ExtraArgs @("--ctx-size", $ctx, "--cache-type-k", "q8_0", "--cache-type-v", "q8_0")
        $monitor = $null
        try {
            $monitor = Start-VramMonitor
            $fillPrompt = New-FillerPrompt -TargetTokens ($ctx - 256)
            $result = Invoke-Completion -Prompt $fillPrompt -NPredict 128
            $vram = Stop-VramMonitor -Job $monitor
            $monitor = $null
            $marginMB = $total - $vram.MaxMB
            Write-Host "Peak VRAM used: $($vram.MaxMB) MB, margin to total: $marginMB MB"
            $rows += Format-Row @("$ctx", "$($vram.MaxMB)", "$marginMB", "$(if ($ctx -eq 8192) { if ($marginMB -ge 300) { 'PASS' } else { 'FAIL' } } else { 'N/A (expected spill check)' })")
        } catch {
            Write-Host "ctx-size $ctx failed: $($_.Exception.Message)" -ForegroundColor Red
            if ($monitor) { Stop-VramMonitor -Job $monitor | Out-Null }
            $rows += Format-Row @("$ctx", "crash/error", "n/a", "FAIL — $($_.Exception.Message)")
        } finally {
            Stop-LlamaServer -Proc $proc
        }
    }

    $md = Get-RunHeader "A1: VRAM headroom"
    $md += "Idle min free VRAM: **$idleFreeMB MB** (of $total MB total, idle window ${idleSeconds}s)`n`n"
    $md += "| ctx-size | Peak VRAM used (MB) | Margin to total (MB) | Result |`n"
    $md += "|---|---|---|---|`n"
    $md += ($rows -join "`n") + "`n"
    Add-ResultSection $md
    Write-Host "A1 results appended to $OutFile" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# A2 / A3. Throughput baseline + speculative decode payoff
# ---------------------------------------------------------------------------

function Invoke-ThroughputSweep {
    param([switch]$WithDraft)

    $lengthPrompts = @(500, 1000, 2000, 4000, 8000) | ForEach-Object {
        [pscustomobject]@{ Id = "len-$_"; Label = "$_ tok"; Prompt = (New-FillerPrompt -TargetTokens $_) }
    }

    $rows = @()
    foreach ($p in $lengthPrompts) {
        $r = Invoke-Completion -Prompt $p.Prompt -NPredict 256
        $rows += [pscustomobject]@{
            Id = $p.Id; Label = $p.Label
            PromptN = $r.PromptN; PromptPerSecond = $r.PromptPerSecond
            PredictedN = $r.PredictedN; PredictedPerSecond = $r.PredictedPerSecond
            DraftN = $r.DraftN; DraftNAccepted = $r.DraftNAccepted
        }
    }

    if ($WithDraft) {
        $csTasks = Get-Content "$PromptsDir/csharp-tasks.json" -Raw | ConvertFrom-Json
        foreach ($t in $csTasks) {
            $r = Invoke-Completion -Prompt $t.prompt -NPredict 400
            $rows += [pscustomobject]@{
                Id = $t.id; Label = $t.label
                PromptN = $r.PromptN; PromptPerSecond = $r.PromptPerSecond
                PredictedN = $r.PredictedN; PredictedPerSecond = $r.PredictedPerSecond
                DraftN = $r.DraftN; DraftNAccepted = $r.DraftNAccepted
            }
        }
    }

    return $rows
}

function Invoke-A2 {
    if (-not $ModelPath) { throw "A2 requires -ModelPath" }
    Write-Host "== A2: Throughput baseline (no draft) ==" -ForegroundColor Yellow

    $proc = Start-LlamaServer -ExtraArgs @("--ctx-size", "8192", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0")
    try {
        $rows = Invoke-ThroughputSweep
    } finally {
        Stop-LlamaServer -Proc $proc
    }

    $md = Get-RunHeader "A2: Throughput baseline (no draft)"
    $md += "| Prompt | prompt tok/s | gen tok/s |`n|---|---|---|`n"
    foreach ($r in $rows) {
        $md += (Format-Row @($r.Label, "$([math]::Round($r.PromptPerSecond,1))", "$([math]::Round($r.PredictedPerSecond,1))")) + "`n"
    }
    Add-ResultSection $md
    $script:A2Rows = $rows
    Write-Host "A2 results appended to $OutFile" -ForegroundColor Green
}

function Invoke-A3 {
    if (-not $ModelPath) { throw "A3 requires -ModelPath" }
    if (-not $SimpleModelServerPath) { throw "A3 requires -SimpleModelServerPath" }
    Write-Host "== A3: Speculative decode payoff ==" -ForegroundColor Yellow

    $proc = Start-LlamaServer -ExtraArgs @(
        "--ctx-size", "8192", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
        "--model-draft", $SimpleModelServerPath, "--spec-draft-n-max", "8"
    )
    try {
        $rows = Invoke-ThroughputSweep -WithDraft
    } finally {
        Stop-LlamaServer -Proc $proc
    }

    # Pull baseline gen tok/s for the C# task set from A2, if it was run in this session.
    $csRows = $rows | Where-Object { $_.Id -notlike "len-*" }

    $md = Get-RunHeader "A3: Speculative decode payoff"
    $md += "| Prompt | gen tok/s (with draft) | draft tokens proposed | draft tokens accepted | acceptance rate |`n"
    $md += "|---|---|---|---|---|`n"
    foreach ($r in $rows) {
        $acc = if ($r.DraftN) { [math]::Round(100.0 * $r.DraftNAccepted / [math]::Max(1,$r.DraftN), 1) } else { "n/a" }
        $md += (Format-Row @($r.Label, "$([math]::Round($r.PredictedPerSecond,1))", "$($r.DraftN)", "$($r.DraftNAccepted)", "$acc%")) + "`n"
    }
    if ($script:A2Rows) {
        $md += "`nSpeedup vs A2 (same session) on the C# task set:`n`n"
        $md += "| Task | A2 gen tok/s | A3 gen tok/s | Speedup |`n|---|---|---|---|`n"
        foreach ($r in $csRows) {
            $base = $script:A2Rows | Where-Object { $_.Id -eq $r.Id }
            if ($base) {
                $speedup = [math]::Round($r.PredictedPerSecond / [math]::Max(0.01,$base.PredictedPerSecond), 2)
                $md += (Format-Row @($r.Label, "$([math]::Round($base.PredictedPerSecond,1))", "$([math]::Round($r.PredictedPerSecond,1))", "${speedup}x")) + "`n"
            }
        }
    } else {
        $md += "`n_Run A2 in the same invocation (-Test All) to get a same-session speedup comparison; otherwise compare manually against the last recorded A2 section above._`n"
    }
    Add-ResultSection $md
    Write-Host "A3 results appended to $OutFile" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# A4. Router latency
# ---------------------------------------------------------------------------

function Invoke-A4 {
    if (-not $RouterModelPath) { throw "A4 requires -RouterModelPath" }
    Write-Host "== A4: Router latency ==" -ForegroundColor Yellow

    $schemaPath = "$PromptsDir/router-schema.json"
    $schema = Get-Content $schemaPath -Raw
    $prompts = Get-Content "$PromptsDir/router-prompts.json" -Raw | ConvertFrom-Json
    $sysPrompt = (Get-Content "$PromptsDir/router-system-prompt.txt" -Raw).Trim()

    # Router runs on CPU per the plan (-ngl 0). Start-LlamaServer is built around the 7B $ModelPath,
    # so the router server is launched directly here instead, pointed at $RouterModelPath.
    $routerArgs = @("--model", $RouterModelPath, "--port", $Port, "--host", "127.0.0.1", "--ctx-size", "4096", "-ngl", "0")
    $proc = Start-Process -FilePath $LlamaServerPath -ArgumentList $routerArgs -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$env:TEMP\llama-server-router.stdout.log" `
        -RedirectStandardError "$env:TEMP\llama-server-router.stderr.log"
    Wait-ServerHealthy | Out-Null

    try {
        $latencies = @()
        $validCount = 0
        $rows = @()
        foreach ($p in $prompts) {
            $fullPrompt = "$sysPrompt`n`nUser message: $($p.prompt)`n`nJSON:"
            $body = @{ json_schema = ($schema | ConvertFrom-Json) }
            $r = Invoke-Completion -Prompt $fullPrompt -NPredict 200 -Temperature 0.0 -ExtraBody $body
            $latencies += $r.WallClockMs

            $isValid = $false
            $parsedIntent = $null
            try {
                $parsed = $r.Content | ConvertFrom-Json
                $parsedIntent = $parsed.intent
                if ($parsed.intent -and $parsed.quality -and $parsed.ambiguity -and $parsed.routing_reason) {
                    $isValid = $true
                    $validCount++
                }
            } catch { $isValid = $false }

            $rows += [pscustomobject]@{
                Id = $p.id; ExpectedIntent = $p.expected_intent; ParsedIntent = $parsedIntent
                Valid = $isValid; LatencyMs = [math]::Round($r.WallClockMs,0)
            }
        }
    } finally {
        Stop-LlamaServer -Proc $proc
    }

    $sorted = $latencies | Sort-Object
    $p50 = $sorted[[math]::Floor(($sorted.Count - 1) * 0.5)]
    $p95 = $sorted[[math]::Floor(($sorted.Count - 1) * 0.95)]
    $validityRate = [math]::Round(100.0 * $validCount / $prompts.Count, 1)
    $intentMatches = ($rows | Where-Object { $_.ParsedIntent -eq $_.ExpectedIntent }).Count
    $intentAccuracy = [math]::Round(100.0 * $intentMatches / $prompts.Count, 1)

    $md = Get-RunHeader "A4: Router latency"
    $md += "p50: **$([math]::Round($p50,0)) ms**, p95: **$([math]::Round($p95,0)) ms**`n`n"
    $md += "Schema validity: **$validCount / $($prompts.Count)** ($validityRate%)`n`n"
    $md += "Intent match vs expected label (informal, not the golden set): **$intentMatches / $($prompts.Count)** ($intentAccuracy%)`n`n"
    $md += "Result: " + $(if ($p95 -lt 1500) { "**PASS** — router runs on every request." } else { "**FAIL** — add heuristic pre-filter, router only on inconclusive." }) + "`n`n"
    $md += "| Prompt id | Expected intent | Parsed intent | Valid | Latency (ms) |`n|---|---|---|---|---|`n"
    foreach ($r in $rows) {
        $md += (Format-Row @($r.Id, $r.ExpectedIntent, "$($r.ParsedIntent)", "$($r.Valid)", "$($r.LatencyMs)")) + "`n"
    }
    Add-ResultSection $md
    Write-Host "A4 results appended to $OutFile" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Write-Host "Prep reminder: open a browser + IDE for realistic desktop load before A1." -ForegroundColor Magenta

function Invoke-TestSafely {
    param([string]$Name, [scriptblock]$Block)
    try {
        & $Block
    } catch {
        Write-Host "$Name FAILED: $($_.Exception.Message)" -ForegroundColor Red
        Add-ResultSection ((Get-RunHeader "$Name — FAILED") + "Error: $($_.Exception.Message)`n")
    }
}

switch ($Test) {
    "A1" { Invoke-A1 }
    "A2" { Invoke-A2 }
    "A3" { Invoke-A3 }
    "A4" { Invoke-A4 }
    "All" {
        # Each test runs independently so a hard crash in one (e.g. GPU device-lost) doesn't
        # prevent the others from producing data.
        Invoke-TestSafely "A1" { Invoke-A1 }
        Invoke-TestSafely "A2" { Invoke-A2 }
        Invoke-TestSafely "A3" { Invoke-A3 }
        Invoke-TestSafely "A4" { Invoke-A4 }
    }
}

Write-Host "`nDone. Results in $OutFile" -ForegroundColor Green
Write-Host "Note: A5 (classification accuracy) requires the Part B golden set (tests/golden/), which doesn't exist yet." -ForegroundColor Yellow
