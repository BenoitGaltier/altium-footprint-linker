param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $env:DOTNET_CLI_HOME = Join-Path $repo '.artifacts/dotnet-home'
    $env:DOTNET_NOLOGO = '1'
    node scripts/vendor.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Run npm ci before publishing' }
    $output = Join-Path $repo "dist/$Runtime"
    $version = ([xml](Get-Content -LiteralPath 'src/FootprintLinker/FootprintLinker.csproj' -Raw)).Project.PropertyGroup.Version
    dotnet publish src/FootprintLinker/FootprintLinker.csproj -c Release -r $Runtime --self-contained true -o $output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Portable build failed' }
    Copy-Item -LiteralPath (Join-Path $repo 'docs/QUICKSTART.txt') -Destination $output
    Copy-Item -LiteralPath (Join-Path $repo 'examples/footprints.csv') -Destination $output
    Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $output
    $licenses = Join-Path $output 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo 'docs/licenses') -File | Copy-Item -Destination $licenses -Force
    $archive = Join-Path $repo "dist/FootprintLinker-$version-$Runtime.zip"
    Compress-Archive -LiteralPath (Join-Path $output 'FootprintLinker.exe'),(Join-Path $output 'QUICKSTART.txt'),(Join-Path $output 'footprints.csv'),(Join-Path $output 'THIRD_PARTY_NOTICES.md'),$licenses -DestinationPath $archive -Force
    $checksum = Get-FileHash -Algorithm SHA256 -LiteralPath $archive
    "$($checksum.Hash.ToLowerInvariant())  $(Split-Path -Leaf $archive)" | Set-Content -Encoding utf8 -LiteralPath "$archive.sha256"
    Write-Output $archive
} finally { Pop-Location }
