#requires -Version 7.0
<#
.SYNOPSIS
    Driver for chained-pipeline-benchmarks.ps1 — runs each (Test, Variant) combination as its own
    isolated child process, one at a time, until every combination is done.

.DESCRIPTION
    chained-pipeline-benchmarks.ps1 -Test All -Variant Both runs everything in a single process,
    which showed a real artifact in the first run: the very first GPU model load (B1-Base) measured
    13-14 tok/s vs the 58-65 tok/s baseline from earlier benchmarks, almost certainly one-time
    Vulkan/CUDA shader-compile warm-up cost that later combinations in the same process didn't pay.
    Running each combo as its own fresh process gives every combo the same cold-start conditions
    (still not zero warm-up, but at least consistent across combos instead of only the first one
    eating it) and means one combo crashing doesn't take down the rest of the run.

    Also cleans up stray llama-server processes and gives the GPU a moment to release VRAM between
    combos, since starting the next model immediately after stopping the last one was a suspected
    contributor to the same slow-first-run artifact.

.PARAMETER Tests
    Which tests to run. Default both B1 and B2.

.PARAMETER Variants
    Which model variants to run. Default both Base and Instruct.

.PARAMETER SkipExisting
    Skip a (Test, Variant) combo if all 5 of its raw-output files already exist in RawOutDir — makes
    the driver resumable after a partial run or a crash, so re-running it just picks up where it
    left off instead of redoing completed combos.

.PARAMETER MaxRetries
    How many times to retry a single combo if the child script exits non-zero before giving up on it
    and moving to the next one. Default 1 (no retry).

.EXAMPLE
    # Run everything, resuming any combos already completed
    ./run-chained-benchmarks.ps1 -SkipExisting

.EXAMPLE
    # Just the two B1 variants
    ./run-chained-benchmarks.ps1 -Tests B1 -Variants Base,Instruct
#>

[CmdletBinding()]
param(
    [ValidateSet("B1", "B2")]
    [string[]]$Tests = @("B1", "B2"),

    [ValidateSet("Base", "Instruct")]
    [string[]]$Variants = @("Base", "Instruct"),

    [switch]$SkipExisting,
    [int]$MaxRetries = 1,
    [int]$SettleSeconds = 5,

    [string]$LlamaServerPath = "llama-server",
    [string]$ModelsDir = "D:\llm-models",
    [int]$Port = 8080,
    [int]$RouterPort = 8081,
    [string]$OutFile = "$PSScriptRoot/../../docs/benchmarks/phase0-results.md",
    [string]$PromptsDir = "$PSScriptRoot/../../tests/prompts",
    [string]$RawOutDir = "$PSScriptRoot/../../tests/quality-check"
)

$ErrorActionPreference = "Stop"
$ChildScript = "$PSScriptRoot/chained-pipeline-benchmarks.ps1"

if (-not (Test-Path $ChildScript)) {
    throw "Could not find chained-pipeline-benchmarks.ps1 next to this script at $ChildScript"
}

function Write-Step {
    param([string]$Message, [string]$Color = "Yellow")
    Write-Host "`n[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Stop-StrayLlamaServers {
    $procs = Get-Process llama-server -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "Stopping $($procs.Count) leftover llama-server process(es)..." -ForegroundColor DarkYellow
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

function Get-ExpectedOutputFiles {
    param([string]$TestName, [string]$VariantName)
    $tasks = Get-Content (Join-Path $PromptsDir "csharp-tasks.json") -Raw | ConvertFrom-Json
    $prefix = if ($TestName -eq "B1") { "b1" } else { "b2" }
    return $tasks | ForEach-Object { Join-Path $RawOutDir "$prefix-$VariantName-$($_.id).txt" }
}

function Test-ComboComplete {
    param([string]$TestName, [string]$VariantName)
    $expected = Get-ExpectedOutputFiles -TestName $TestName -VariantName $VariantName
    return ($expected | Where-Object { -not (Test-Path $_) }).Count -eq 0
}

# ---------------------------------------------------------------------------
# Build the combo queue and run one at a time
# ---------------------------------------------------------------------------

$combos = foreach ($t in $Tests) { foreach ($v in $Variants) { [pscustomobject]@{ Test = $t; Variant = $v } } }

Write-Host "Queue: $($combos.Count) combo(s) — $(($combos | ForEach-Object { "$($_.Test)/$($_.Variant)" }) -join ', ')" -ForegroundColor Cyan

$results = @()
$index = 0

foreach ($combo in $combos) {
    $index++
    $label = "$($combo.Test)/$($combo.Variant)"
    Write-Step "[$index/$($combos.Count)] $label"

    if ($SkipExisting -and (Test-ComboComplete -TestName $combo.Test -VariantName $combo.Variant)) {
        Write-Host "All 5 output files already exist — skipping." -ForegroundColor DarkGray
        $results += [pscustomobject]@{ Combo = $label; Status = "Skipped (already complete)"; Attempts = 0 }
        continue
    }

    Stop-StrayLlamaServers

    $succeeded = $false
    $attempts = 0
    while (-not $succeeded -and $attempts -lt $MaxRetries) {
        $attempts++
        Write-Host "Attempt $attempts/$MaxRetries..." -ForegroundColor Cyan

        & pwsh -NoProfile -ExecutionPolicy Bypass -File $ChildScript `
            -Test $combo.Test -Variant $combo.Variant `
            -LlamaServerPath $LlamaServerPath -ModelsDir $ModelsDir `
            -Port $Port -RouterPort $RouterPort -OutFile $OutFile `
            -PromptsDir $PromptsDir -RawOutDir $RawOutDir
        $exitCode = $LASTEXITCODE

        Stop-StrayLlamaServers

        if ($exitCode -eq 0 -and (Test-ComboComplete -TestName $combo.Test -VariantName $combo.Variant)) {
            $succeeded = $true
        } else {
            Write-Host "$label attempt $attempts did not produce all 5 output files (exit code $exitCode)." -ForegroundColor Red
        }
    }

    $results += [pscustomobject]@{
        Combo    = $label
        Status   = if ($succeeded) { "Done" } else { "FAILED after $attempts attempt(s)" }
        Attempts = $attempts
    }

    if ($index -lt $combos.Count) {
        Write-Host "Settling $SettleSeconds s before the next combo (let VRAM release fully)..." -ForegroundColor DarkGray
        Start-Sleep -Seconds $SettleSeconds
    }
}

Write-Step "All combos processed." "Green"
$results | Format-Table -AutoSize | Out-String | Write-Host

$failed = $results | Where-Object { $_.Status -like "FAILED*" }
if ($failed) {
    Write-Host "$($failed.Count) combo(s) failed — see above. Re-run with -SkipExisting to retry only what's missing." -ForegroundColor Red
    exit 1
}

Write-Host "Done. Results in $OutFile" -ForegroundColor Green
