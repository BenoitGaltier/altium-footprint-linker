$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifact = Join-Path $repo '.artifacts/smoke'
New-Item -ItemType Directory -Path $artifact -Force | Out-Null
$binary = Join-Path $repo 'dist/win-x64/FootprintLinker.exe'
$output = Join-Path $artifact 'stdout.txt'
$errorOutput = Join-Path $artifact 'stderr.txt'
$data = Join-Path $artifact ('data-' + [guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath $binary -ArgumentList @('--headless','--data-dir',('"' + $data + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $output -RedirectStandardError $errorOutput
try {
    $url = $null
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($process.HasExited) { throw "Portable app exited: $(Get-Content $errorOutput -Raw)" }
        $logText = [string](Get-Content $output -Raw -ErrorAction SilentlyContinue)
        $match = [regex]::Match($logText, 'http://127\.0\.0\.1:\d+/#session=[A-F0-9]+')
        if ($match.Success) { $url = $match.Value; break }
        Start-Sleep -Milliseconds 100
    }
    if (!$url) { throw 'App did not start' }
    $base = $url.Split('/#')[0]
    $headers = @{ 'X-Linker-Session' = $url.Split('=')[-1] }
    $checks = 0
    foreach ($asset in @('/','/app.mjs','/core.mjs','/style.css','/favicon.svg')) {
        $response = Invoke-WebRequest -Uri ($base + $asset)
        if ($response.StatusCode -ne 200 -or !$response.Content.Length) { throw "Asset missing: $asset" }
        $checks++
    }
    try { Invoke-WebRequest -Uri "$base/api/session" | Out-Null; throw 'Anonymous access accepted' } catch { if ([int]$_.Exception.Response.StatusCode -ne 403) { throw } }; $checks++
    $state = Invoke-RestMethod -Uri "$base/api/session" -Headers $headers
    if ($state.connection -or $state.journal.Count) { throw 'Fixture state was not isolated' }; $checks++
    try { Invoke-WebRequest -Uri "$base/api/session" -Headers ($headers + @{Origin='https://example.invalid'}) | Out-Null; throw 'Foreign origin accepted' } catch { if ([int]$_.Exception.Response.StatusCode -ne 403) { throw } }; $checks++
    Invoke-RestMethod -Method Post -Uri "$base/api/quit" -Headers $headers -ContentType 'application/json' -Body '{}' | Out-Null
    $checks++
    if (!$process.WaitForExit(10000)) { throw 'App did not quit' }
    if ($process.ExitCode -ne 0) { throw 'App failed on exit' }
    Write-Output "$checks portable HTTP checks passed"
} finally { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
