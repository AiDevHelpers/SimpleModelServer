$dir = "F:\mcp\SimpleModelServer\tests\quality-check-v2-base"
$t1 = Get-Content F:\mcp\SimpleModelServer\tests\prompts\csharp-tasks.json -Raw | ConvertFrom-Json
$t2 = Get-Content F:\mcp\SimpleModelServer\tests\prompts\csharp-tasks-v2.json -Raw | ConvertFrom-Json
$all = @($t1) + @($t2)
foreach ($t in $all) {
  $body = @{ prompt = $t.prompt; n_predict = 900; temperature = 0.2 } | ConvertTo-Json
  try {
    $resp = Invoke-RestMethod -Uri "http://127.0.0.1:8080/completion" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 500
    $resp.content | Out-File -FilePath "$dir\$($t.id).txt" -Encoding utf8
    Add-Content -Path "$dir\generate.log" -Value "OK: $($t.id)"
  } catch {
    Add-Content -Path "$dir\generate.log" -Value "ERROR: $($t.id): $_"
  }
}
Add-Content -Path "$dir\generate.log" -Value "DONE"
