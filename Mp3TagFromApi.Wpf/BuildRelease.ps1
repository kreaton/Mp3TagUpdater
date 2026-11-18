# BuildRelease.ps1
# Builds the next version of Mp3TagFromApi, packs it with Velopack and publishes it as a GitHub release.
# Mp3TagFromApi is its own package: the releases go to a separate, release-only repository, so that
# "releases/latest" in the Mp3TagUpdater repository keeps pointing at Mp3TagUpdater. Nothing runs this
# automatically; run it by hand.
#   .\BuildRelease.ps1             build, pack and upload
#   .\BuildRelease.ps1 -LocalOnly  build and pack into .\Releases only
#
# Requires the vpk tool, matching the Velopack package version in the csproj:
#   dotnet tool install -g vpk --version 1.2.161
# and, unless -LocalOnly, a GitHub token: $env:GH_TOKEN, otherwise the GitHub CLI (gh auth login).
# The releases repository needs at least one commit (a README will do) before the first upload.
param([switch]$LocalOnly)

Set-Location -Path $PSScriptRoot
$ErrorActionPreference = 'Stop'

$AppName = "Mp3TagFromApi"
$ExeName = "Mp3TagFromApi.exe"
$RepoUrl = "https://github.com/kreaton/Mp3TagFromApi"   # keep in step with SettingsWindow.ReleasesUrl
$FirstVersion = "1.0.0"
$PublishDir = ".\publish"
$ReleasesDir = ".\Releases"

if (-not $LocalOnly) {
    # Fetch the latest published release so the version continues from it and vpk can build a delta
    $token = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { gh auth token }
    if (-not $token) { throw "No GitHub token (set GH_TOKEN or run: gh auth login)" }
    vpk download github --repoUrl $RepoUrl --token $token -o $ReleasesDir
    if ($LASTEXITCODE -ne 0) { throw "vpk download failed" }
}

# Find the highest version already packed. It is read from the full package's file name because
# vpk download only fetches the package: a clean Releases folder has no releases.win.json.
$largestVersion = Get-ChildItem -Path $ReleasesDir -Filter "$AppName-*-full.nupkg" -ErrorAction SilentlyContinue
    | ForEach-Object { [version]($_.Name -replace "^$AppName-|-full\.nupkg$") }
    | Sort-Object -Descending
    | Select-Object -First 1
if ($largestVersion) {
    # Increment the patch number by 1
    $newVersion = "{0}.{1}.{2}" -f $largestVersion.Major, $largestVersion.Minor, ($largestVersion.Build + 1)
}
else {
    $newVersion = $FirstVersion
}

Write-Host "Publishing $AppName version $newVersion (framework-dependent, win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish -c Release -r win-x64 --self-contained false -p:Version=$newVersion -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Packing $AppName version $newVersion with Velopack"
# --framework makes Setup.exe install the .NET 10 Desktop Runtime if it is missing
vpk pack -u $AppName -v $newVersion -p $PublishDir -e $ExeName -o $ReleasesDir --icon .\app.ico --runtime win-x64 --framework net10.0-x64-desktop
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if (-not $LocalOnly) {
    Write-Host "Uploading $AppName version $newVersion to GitHub"
    # The tag is created on the releases repository's default branch, since the source lives elsewhere
    vpk upload github --repoUrl $RepoUrl --token $token -o $ReleasesDir --publish --tag "v$newVersion" --releaseName "$AppName $newVersion"
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
}

Write-Host "Built $AppName version $newVersion"
