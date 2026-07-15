<#
.SYNOPSIS
  Minimal compile-gate prototype: extracts C# code from raw LLM completions and
  attempts to compile each one via `dotnet build`, reporting pass/fail + errors.

.DESCRIPTION
  Phase 0 finding (2026-07-07): both Qwen2.5-Coder-3B base and Instruct produced
  code that doesn't compile on a meaningful fraction of realistic tasks. This
  script is the first working version of the compile-gate the staged pipeline
  plan already calls for in Phase 4 — used here to get a real defect-rate number
  instead of one-off manual dotnet build checks.

.PARAMETER InputDir
  Directory of *.txt files, one per task, containing raw completion text
  (may include prose + a ```csharp fenced block, or raw code).

.PARAMETER OutputCsv
  Where to write the per-task pass/fail report.

.EXAMPLE
  ./compile-gate.ps1 -InputDir ..\..\tests\quality-check-v2 -OutputCsv ..\..\tests\quality-check-v2\results.csv
#>
param(
    [Parameter(Mandatory = $true)][string]$InputDir,
    [Parameter(Mandatory = $true)][string]$OutputCsv
)

$ErrorActionPreference = "Stop"

$scratchDir = Join-Path $InputDir "_compile_scratch"
if (-not (Test-Path $scratchDir)) {
    dotnet new console -o $scratchDir --force | Out-Null
}

$standardUsings = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

"@

function Extract-CSharpCode {
    param([string]$RawText)

    # Prefer a fenced ```csharp ... ``` or ```cs ... ``` block if present.
    $fenceMatch = [regex]::Match($RawText, '```(?:csharp|cs)?\s*(.*?)```', 'Singleline')
    if ($fenceMatch.Success) {
        return $fenceMatch.Groups[1].Value.Trim()
    }
    # No fence found — assume the whole response is code (base-model raw completions
    # sometimes have no fences at all).
    return $RawText.Trim()
}

function Needs-EntryPoint {
    param([string]$Code)
    return -not ($Code -match 'static\s+(async\s+)?(void|Task)\s+Main\s*\(')
}

$results = New-Object System.Collections.Generic.List[object]

$files = Get-ChildItem -Path $InputDir -Filter "*.txt" | Sort-Object Name
foreach ($f in $files) {
    $id = $f.BaseName
    $raw = Get-Content -Path $f.FullName -Raw
    $code = Extract-CSharpCode -RawText $raw

    if ([string]::IsNullOrWhiteSpace($code)) {
        $results.Add([PSCustomObject]@{ Id = $id; Result = "NO_CODE_EXTRACTED"; Errors = "" })
        continue
    }

    $entryPoint = ""
    if (Needs-EntryPoint -Code $code) {
        $entryPoint = "`n`nclass __CompileGateEntry { static void Main() { } }"
    }

    $fullSource = $standardUsings + $code + $entryPoint
    Set-Content -Path (Join-Path $scratchDir "Program.cs") -Value $fullSource -Encoding utf8

    Push-Location $scratchDir
    try {
        $buildOutput = dotnet build --nologo 2>&1 | Out-String
    } finally {
        Pop-Location
    }

    if ($buildOutput -match "Build succeeded") {
        $results.Add([PSCustomObject]@{ Id = $id; Result = "PASS"; Errors = "" })
    } else {
        $errorLines = ($buildOutput -split "`r?`n" | Where-Object { $_ -match "error CS" }) -join " | "
        $results.Add([PSCustomObject]@{ Id = $id; Result = "FAIL"; Errors = $errorLines })
    }
}

$results | Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding utf8

$pass = ($results | Where-Object { $_.Result -eq "PASS" }).Count
$total = $results.Count
Write-Output "Compile-gate: $pass / $total passed"
$results | Format-Table -AutoSize
