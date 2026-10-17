param(
    [string]$InnoCompiler,
    [string]$MicrosoftRuntime,
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $repository
if (-not (Test-Path -LiteralPath 'BarberBooking.slnx')) { throw 'Repository not found.' }
if ($Version -ne '1.0.0') { throw 'Update application, installer and documentation versions before changing the release version.' }
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Pass -InnoCompiler with the path to the official Inno Setup 6 compiler.' }
if (-not $MicrosoftRuntime -or -not (Test-Path -LiteralPath $MicrosoftRuntime)) { throw 'Pass -MicrosoftRuntime with the official VC++ 14.44.35211 x64 redistributable.' }
function Hash([string]$file) { $stream=[IO.File]::OpenRead($file); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() } }
if ((Hash $MicrosoftRuntime) -ne 'cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b') { throw 'VC++ SHA-256 mismatch.' }
if ((Get-AuthenticodeSignature -LiteralPath $MicrosoftRuntime).Status -ne 'Valid') { throw 'VC++ Microsoft signature is invalid.' }
$stage = Join-Path $repository ('.artifacts/release-stage-' + [Guid]::NewGuid().ToString('N'))
$windows = Join-Path $stage 'Bladez-Windows'
$output = Join-Path $repository 'release'
New-Item -ItemType Directory -Force -Path $windows,$output | Out-Null
dotnet restore BarberBooking.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet publish BarberBooking.Web -c Release -r win-x64 --self-contained true --no-restore -o (Join-Path $windows 'server')
if ($LASTEXITCODE -ne 0) { throw 'Web publish failed.' }
dotnet publish Bladez.Host -c Release -r win-x64 --self-contained true --no-restore -o $windows
if ($LASTEXITCODE -ne 0) { throw 'Host publish failed.' }
New-Item -ItemType Directory -Path (Join-Path $windows 'prerequisites') | Out-Null
Copy-Item -LiteralPath $MicrosoftRuntime -Destination (Join-Path $windows 'prerequisites/vc_redist.x64.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'START-HERE.html') -Destination $windows
Copy-Item -LiteralPath (Join-Path $repository 'docs/USER-GUIDE.md'),(Join-Path $repository 'docs/SECURITY.md'),(Join-Path $repository 'docs/THIRD-PARTY.md') -Destination $windows
$commit = (& git rev-parse HEAD).Trim()
@{version=$Version;commit=$commit;builtUtc=[DateTime]::UtcNow.ToString('o');mysql='8.4.10';mysqlSha256='3b950db31c33fb59252568c012bd9ee5fac50811e778ca7c8f1a0dc91686cd6f';vcRuntimeSha256=(Hash $MicrosoftRuntime)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
& $InnoCompiler "/DReleaseRoot=$windows" "/DOutputRoot=$output" (Join-Path $PSScriptRoot 'setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed.' }
$zip = Join-Path $output "Bladez-Windows-$Version.zip"
if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists; choose a clean output directory explicitly before rerunning.' }
Compress-Archive -Path (Join-Path $windows '*') -DestinationPath $zip -CompressionLevel Optimal
$source = Join-Path $output "Bladez-Source-$Version.zip"
git -c core.autocrlf=false archive --format=zip --prefix="Bladez-$Version/" --output=$source HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
# Source archive includes the novice Linux host installer; no SDK is required on the VPS (Docker builds it).
$files = @("Bladez-Setup-$Version.exe", "Bladez-Windows-$Version.zip", "Bladez-Source-$Version.zip", 'release-manifest.json')
$lines = foreach ($name in $files) { (Hash (Join-Path $output $name)) + '  ' + $name }
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
Write-Output "Built release from $commit. Output: $output"
Write-Output "Stage retained for installer verification: $windows"
