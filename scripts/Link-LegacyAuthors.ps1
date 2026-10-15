[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_.-]*$')]
    [string] $ContainerName,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')]
    [string] $DatabaseName = 'bookstore',
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Manifest must be a file.' }
$script = Join-Path $PSScriptRoot 'link-legacy-authors.js'
if (-not $ContainerName) {
    $containers = @(& docker compose ps --status running -q mongodb)
    if ($LASTEXITCODE -ne 0 -or $containers.Count -ne 1) { throw 'Specify exactly one running MongoDB container.' }
    $ContainerName = $containers[0].Trim()
}

$directory = "/data/author-mapping-$([Guid]::NewGuid().ToString('N'))"
$created = $false
try {
    & docker exec --user 0 $ContainerName sh -c "umask 077; mkdir '$directory'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create protected maintenance directory.' }
    $created = $true
    & docker cp $manifest "${ContainerName}:$directory/manifest.json"
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy manifest.' }
    & docker cp $script "${ContainerName}:$directory/link-legacy-authors.js"
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy maintenance script.' }
    & docker exec --user 0 $ContainerName chmod 600 "$directory/manifest.json" "$directory/link-legacy-authors.js"
    if ($LASTEXITCODE -ne 0) { throw 'Could not protect maintenance files.' }
    $mode = if ($Apply) { 'apply' } else { 'dry-run' }
    & docker exec --user 0 `
        -e "BOOKSTORE_MAPPING_MANIFEST=$directory/manifest.json" `
        -e "BOOKSTORE_MAPPING_MODE=$mode" `
        $ContainerName mongosh "mongodb://127.0.0.1:27017/${DatabaseName}?directConnection=true&replicaSet=rs0" `
        --quiet --file "$directory/link-legacy-authors.js"
    if ($LASTEXITCODE -ne 0) { throw 'Mapping failed; review safe progress codes. Earlier committed entries can be resumed.' }
}
finally {
    if ($created) {
        & docker exec --user 0 $ContainerName rm -f "$directory/manifest.json" "$directory/link-legacy-authors.js"
        $removeResult = $LASTEXITCODE
        & docker exec --user 0 $ContainerName rmdir $directory
        if ($removeResult -ne 0 -or $LASTEXITCODE -ne 0) {
            Write-Warning "Cleanup failed for owned maintenance directory $directory; remove its two files securely."
        }
    }
}
