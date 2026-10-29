$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $env:DOTNET_CLI_HOME = Join-Path $repo '.artifacts/dotnet-home'
    $env:DOTNET_NOLOGO = '1'
    dotnet run --project tests/FootprintLinker.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed' }
    node --test tests/web.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed' }
} finally { Pop-Location }
