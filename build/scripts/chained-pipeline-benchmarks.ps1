#requires -Version 7.0
<#
.SYNOPSIS
    Basic benchmarks for two chained-pipeline shapes, both starting from the embedded (1.5B)
    classify+rewrite stage:

      B1: emb -> draft pair       (1.5B classifies/rewrites, then 0.5B+3B speculative pair generates)
      B2: emb -> 0.5 rewrite -> 3B do
                                   (1.5B classifies, then 0.5B-instruct rewrites/expands the prompt
                                    into a more detailed spec, then 3B alone — no speculative decode —
                                    generates from that expanded spec)

.DESCRIPTION
    Reuses the same 5 prompts from tests/prompts/csharp-tasks.json already used for the direct
    3B+0.5B quality check in docs/benchmarks/phase0-results.md, so B1/B2 are directly comparable to
    that existing baseline. Not part of phase0-benchmarks.ps1's A1-A4 suite — this is a follow-up
    "does chaining the embedded stage in front change latency/quality" experiment, hence a separate
    script. Appends a results section to the same OutFile.

.PARAMETER Variant
    Which model tier to run the 3B/0.5B-draft-pair stages at: Base, Instruct, or Both. The embedded
    (1.5B) stage is always -instruct (no base 1.5B checkpoint exists in this repo's model set), and
    the B2 rewrite stage is always 0.5B-instruct regardless of Variant — an instruction-following
    rewrite task needs an instruction-tuned model; using the base checkpoint there would reproduce
    the exact "base models don't reliably follow instructions" failure mode Phase 0 already
    documented for codegen, except worse since rewriting has no compiler to catch a bad output.

.PARAMETER ModelsDir
    Directory containing the GGUF files. Default D:\llm-models (this machine's convention).

.PARAMETER Port / RouterPort
    Ports for the generation-role server (whichever model currently occupies it) and the embedded
    classifier server, matching the two-port convention documented in src/SimpleModelServer.Evals/README.md.
#>

[CmdletBinding()]
param(
    [ValidateSet("B1", "B2", "All")]
    [string]$Test = "All",

    [ValidateSet("Base", "Instruct", "Both")]
    [string]$Variant = "Both",

    [string]$LlamaServerPath = "llama-server",
    [string]$ModelsDir = "D:\llm-models",
    [int]$Port = 8080,
    [int]$RouterPort = 8081,
    [string]$OutFile = "$PSScriptRoot/../../docs/benchmarks/phase0-results.md",
    [string]$PromptsDir = "$PSScriptRoot/../../tests/prompts",
    [string]$RawOutDir = "$PSScriptRoot/../../tests/quality-check"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Model paths
# ---------------------------------------------------------------------------

$RouterModelPath = Join-Path $ModelsDir "qwen2.5-1.5b-instruct-q8_0.gguf"
$RewriterModelPath = Join-Path $ModelsDir "qwen2.5-coder-0.5b-instruct-q8_0.gguf"

function Get-VariantModelPaths {
    param([string]$V)
    if ($V -eq "Base") {
        return [pscustomobject]@{
            Main  = Join-Path $ModelsDir "qwen2.5-coder-3b-base-q4_k_m.gguf"
            Draft = Join-Path $ModelsDir "qwen2.5-coder-0.5b-base-q8_0.gguf"
        }
    }
    return [pscustomobject]@{
        Main  = Join-Path $ModelsDir "qwen2.5-coder-3b-instruct-q4_k_m.gguf"
        Draft = Join-Path $ModelsDir "qwen2.5-coder-0.5b-instruct-q8_0.gguf"
    }
}

foreach ($p in @($RouterModelPath, $RewriterModelPath)) {
    if (-not (Test-Path $p)) { throw "Model not found: $p" }
}

New-Item -ItemType Directory -Path $RawOutDir -Force | Out-Null

# ---------------------------------------------------------------------------
# Helpers (subset of build/scripts/phase0-benchmarks.ps1's conventions, adapted for two ports)
# ---------------------------------------------------------------------------

function Wait-ServerHealthy {
    param([int]$OnPort, [int]$TimeoutSec = 120)
    $base = "http://127.0.0.1:$OnPort"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "$base/health" -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { return $true }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    throw "llama-server did not become healthy on port $OnPort within $TimeoutSec s"
}

function Start-LlamaServerOn {
    param([string]$ModelPath, [int]$OnPort, [string[]]$ExtraArgs, [string]$Tag)
    $serverArgs = @("--model", $ModelPath, "--port", $OnPort, "--host", "127.0.0.1") + $ExtraArgs
    Write-Host "Starting llama-server ($Tag) on port $OnPort`: $ModelPath" -ForegroundColor Cyan
    $proc = Start-Process -FilePath $LlamaServerPath -ArgumentList $serverArgs -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$env:TEMP\llama-server-$Tag.stdout.log" `
        -RedirectStandardError "$env:TEMP\llama-server-$Tag.stderr.log"
    Wait-ServerHealthy -OnPort $OnPort | Out-Null
    return $proc
}

function Stop-LlamaServerProc {
    param($Proc)
    if ($Proc -and -not $Proc.HasExited) {
        Stop-Process -Id $Proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

function Invoke-CompletionOn {
    param(
        [int]$OnPort,
        [string]$Prompt,
        [int]$NPredict = 700,
        [double]$Temperature = 0.2,
        [hashtable]$ExtraBody = @{}
    )
    $body = @{ prompt = $Prompt; n_predict = $NPredict; temperature = $Temperature; cache_prompt = $false }
    foreach ($k in $ExtraBody.Keys) { $body[$k] = $ExtraBody[$k] }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $resp = Invoke-RestMethod -Uri "http://127.0.0.1:$OnPort/completion" -Method Post `
        -Body ($body | ConvertTo-Json -Depth 10) -ContentType "application/json" -TimeoutSec 300
    $sw.Stop()

    [pscustomobject]@{
        Content            = $resp.content
        WallClockMs        = $sw.Elapsed.TotalMilliseconds
        PredictedN         = $resp.timings.predicted_n
        PredictedPerSecond = $resp.timings.predicted_per_second
        DraftN             = $resp.timings.draft_n
        DraftNAccepted     = $resp.timings.draft_n_accepted
    }
}

function Get-RunHeader {
    param([string]$Section)
    $gitSha = try { (git -C "$PSScriptRoot/../.." rev-parse --short HEAD 2>$null) } catch { "unknown" }
    if (-not $gitSha) { $gitSha = "unknown" }
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss K"
    "`n## $Section — $ts (git $gitSha)`n`n"
}

function Add-ResultSection {
    param([string]$Markdown)
    $dir = Split-Path -Parent $OutFile
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if (-not (Test-Path $OutFile)) {
        "# Phase 0 Benchmark Results`n" | Out-File -FilePath $OutFile -Encoding utf8
    }
    Add-Content -Path $OutFile -Value $Markdown -Encoding utf8
}

function Format-Row {
    param([string[]]$Cells)
    "| " + ($Cells -join " | ") + " |"
}

$RouterSystemPrompt = (Get-Content (Join-Path $PromptsDir "router-system-prompt.txt") -Raw).Trim()

$RewriteSystemPrompt = "Expand the following C# coding request into a precise, detailed technical " +
    "specification: concrete types, method signatures, and edge cases the implementation must " +
    "handle. Output only the rewritten specification, no commentary, no code."

# ---------------------------------------------------------------------------
# Stage E: embedded classify + rewrite (shared by B1 and B2, run once per invocation)
# ---------------------------------------------------------------------------

function Invoke-EmbeddedStage {
    param([pscustomobject[]]$Tasks)

    $schema = Get-Content (Join-Path $PromptsDir "router-schema.json") -Raw | ConvertFrom-Json
    $proc = Start-LlamaServerOn -ModelPath $RouterModelPath -OnPort $RouterPort `
        -ExtraArgs @("--ctx-size", "4096", "-ngl", "0") -Tag "embedded"
    try {
        $results = @()
        foreach ($t in $Tasks) {
            $fullPrompt = "$RouterSystemPrompt`n`nUser message: $($t.prompt)`n`nJSON:"
            $r = Invoke-CompletionOn -OnPort $RouterPort -Prompt $fullPrompt -NPredict 400 -Temperature 0.0 `
                -ExtraBody @{ json_schema = $schema }

            $parsed = $null
            try { $parsed = $r.Content | ConvertFrom-Json } catch { $parsed = $null }

            $results += [pscustomobject]@{
                Id              = $t.id
                OriginalPrompt  = $t.prompt
                RoutingReason   = $parsed.routing_reason
                RewrittenPrompt = $parsed.rewritten_prompt
                EmbedLatencyMs  = $r.WallClockMs
            }
        }
        return $results
    } finally {
        Stop-LlamaServerProc -Proc $proc
    }
}

# ---------------------------------------------------------------------------
# B1: emb -> draft pair
# ---------------------------------------------------------------------------

function Invoke-B1 {
    param([pscustomobject[]]$EmbedResults, [string]$VariantName)

    $paths = Get-VariantModelPaths -V $VariantName
    $proc = Start-LlamaServerOn -ModelPath $paths.Main -OnPort $Port `
        -ExtraArgs @("--ctx-size", "8192", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
                     "--model-draft", $paths.Draft, "--spec-draft-n-max", "8") -Tag "b1-$VariantName"
    try {
        $rows = @()
        foreach ($e in $EmbedResults) {
            $prompt = if ([string]::IsNullOrWhiteSpace($e.RewrittenPrompt)) { $e.OriginalPrompt } else { $e.RewrittenPrompt }
            $r = Invoke-CompletionOn -OnPort $Port -Prompt $prompt -NPredict 700 -Temperature 0.2

            $outFile = Join-Path $RawOutDir "b1-$VariantName-$($e.Id).txt"
            $r.Content | Out-File -FilePath $outFile -Encoding utf8

            $acceptRate = if ($r.DraftN) { [math]::Round(100.0 * $r.DraftNAccepted / [math]::Max(1, $r.DraftN), 1) } else { $null }

            $rows += [pscustomobject]@{
                Id             = $e.Id
                EmbedMs        = [math]::Round($e.EmbedLatencyMs, 0)
                GenMs          = [math]::Round($r.WallClockMs, 0)
                TotalMs        = [math]::Round($e.EmbedLatencyMs + $r.WallClockMs, 0)
                GenTokPerSec   = [math]::Round($r.PredictedPerSecond, 1)
                DraftAcceptPct = $acceptRate
                OutFile        = $outFile
            }
        }
        return $rows
    } finally {
        Stop-LlamaServerProc -Proc $proc
    }
}

# ---------------------------------------------------------------------------
# B2: emb -> 0.5 rewrite -> 3B do (no speculative decode)
# ---------------------------------------------------------------------------

function Invoke-B2 {
    param([pscustomobject[]]$EmbedResults, [string]$VariantName)

    # Rewrite sub-stage: 0.5B-instruct only, regardless of Variant (see script header comment).
    $rewriteProc = Start-LlamaServerOn -ModelPath $RewriterModelPath -OnPort $Port `
        -ExtraArgs @("--ctx-size", "4096") -Tag "b2-rewrite"
    $rewrites = @()
    try {
        foreach ($e in $EmbedResults) {
            $basePrompt = if ([string]::IsNullOrWhiteSpace($e.RewrittenPrompt)) { $e.OriginalPrompt } else { $e.RewrittenPrompt }
            $fullPrompt = "$RewriteSystemPrompt`n`nRequest: $basePrompt`n`nSpecification:"
            $r = Invoke-CompletionOn -OnPort $Port -Prompt $fullPrompt -NPredict 300 -Temperature 0.2
            $rewrites += [pscustomobject]@{ Id = $e.Id; RewriteText = $r.Content.Trim(); RewriteLatencyMs = $r.WallClockMs }
        }
    } finally {
        Stop-LlamaServerProc -Proc $rewriteProc
    }

    # Generation sub-stage: 3B alone, no --model-draft.
    $paths = Get-VariantModelPaths -V $VariantName
    $genProc = Start-LlamaServerOn -ModelPath $paths.Main -OnPort $Port `
        -ExtraArgs @("--ctx-size", "8192", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0") -Tag "b2-gen-$VariantName"
    try {
        $rows = @()
        foreach ($e in $EmbedResults) {
            $rw = $rewrites | Where-Object { $_.Id -eq $e.Id }
            $genPrompt = if ([string]::IsNullOrWhiteSpace($rw.RewriteText)) {
                if ([string]::IsNullOrWhiteSpace($e.RewrittenPrompt)) { $e.OriginalPrompt } else { $e.RewrittenPrompt }
            } else {
                $rw.RewriteText
            }

            $r = Invoke-CompletionOn -OnPort $Port -Prompt $genPrompt -NPredict 700 -Temperature 0.2

            $outFile = Join-Path $RawOutDir "b2-$VariantName-$($e.Id).txt"
            $r.Content | Out-File -FilePath $outFile -Encoding utf8

            $rows += [pscustomobject]@{
                Id           = $e.Id
                EmbedMs      = [math]::Round($e.EmbedLatencyMs, 0)
                RewriteMs    = [math]::Round($rw.RewriteLatencyMs, 0)
                GenMs        = [math]::Round($r.WallClockMs, 0)
                TotalMs      = [math]::Round($e.EmbedLatencyMs + $rw.RewriteLatencyMs + $r.WallClockMs, 0)
                GenTokPerSec = [math]::Round($r.PredictedPerSecond, 1)
                OutFile      = $outFile
            }
        }
        return $rows
    } finally {
        Stop-LlamaServerProc -Proc $genProc
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$tasks = Get-Content (Join-Path $PromptsDir "csharp-tasks.json") -Raw | ConvertFrom-Json
$variants = if ($Variant -eq "Both") { @("Base", "Instruct") } else { @($Variant) }

function Invoke-TestSafely {
    param([string]$Name, [scriptblock]$Block)
    try {
        & $Block
    } catch {
        Write-Host "$Name FAILED: $($_.Exception.Message)" -ForegroundColor Red
        Add-ResultSection ((Get-RunHeader "$Name — FAILED") + "Error: $($_.Exception.Message)`n")
    }
}

foreach ($v in $variants) {
    Write-Host "`n=== Variant: $v ===" -ForegroundColor Yellow

    Write-Host "-- Stage E: embedded classify+rewrite (shared prerequisite for B1/B2) --" -ForegroundColor Yellow
    $embedResults = Invoke-EmbeddedStage -Tasks $tasks

    # B1 and B2 are independent tests — each writes its own report section and can be run on its
    # own via -Test B1 / -Test B2, matching the A1-A4 convention in phase0-benchmarks.ps1. They
    # both consume the same Stage E classify+rewrite output so that stage only runs once per variant
    # regardless of which test(s) are selected.

    if ($Test -in @("B1", "All")) {
        Invoke-TestSafely "B1 ($v)" {
            Write-Host "-- B1: emb -> draft pair --" -ForegroundColor Yellow
            $b1 = Invoke-B1 -EmbedResults $embedResults -VariantName $v

            $md = Get-RunHeader "B1: emb -> draft pair ($v)"
            $md += "5 tasks from tests/prompts/csharp-tasks.json. Embedded (1.5B-instruct) classifies+rewrites, "
            $md += "then 0.5B-$v draft + 3B-$v main generate via speculative decode.`n`n"
            $md += "| Task | Embed (ms) | Gen (ms) | Total (ms) | Gen tok/s | Draft accept % |`n|---|---|---|---|---|---|`n"
            foreach ($r in $b1) {
                $md += (Format-Row @($r.Id, "$($r.EmbedMs)", "$($r.GenMs)", "$($r.TotalMs)", "$($r.GenTokPerSec)", "$($r.DraftAcceptPct)")) + "`n"
            }
            $md += "`nRaw outputs: tests/quality-check/b1-$v-*.txt`n"
            Add-ResultSection $md
            Write-Host "B1 ($v) results appended to $OutFile" -ForegroundColor Green
        }
    }

    if ($Test -in @("B2", "All")) {
        Invoke-TestSafely "B2 ($v)" {
            Write-Host "-- B2: emb -> 0.5 rewrite -> 3B do --" -ForegroundColor Yellow
            $b2 = Invoke-B2 -EmbedResults $embedResults -VariantName $v

            $md = Get-RunHeader "B2: emb -> 0.5B rewrite -> 3B do ($v)"
            $md += "5 tasks from tests/prompts/csharp-tasks.json. Embedded (1.5B-instruct) classifies+rewrites, "
            $md += "then 0.5B-instruct expands the prompt into a detailed spec, then 3B-$v alone (no speculative decode) generates from that spec.`n`n"
            $md += "| Task | Embed (ms) | Rewrite (ms) | Gen (ms) | Total (ms) | Gen tok/s |`n|---|---|---|---|---|---|`n"
            foreach ($r in $b2) {
                $md += (Format-Row @($r.Id, "$($r.EmbedMs)", "$($r.RewriteMs)", "$($r.GenMs)", "$($r.TotalMs)", "$($r.GenTokPerSec)")) + "`n"
            }
            $md += "`nRaw outputs: tests/quality-check/b2-$v-*.txt`n"
            Add-ResultSection $md
            Write-Host "B2 ($v) results appended to $OutFile" -ForegroundColor Green
        }
    }
}

Write-Host "`nDone. Results in $OutFile" -ForegroundColor Green
