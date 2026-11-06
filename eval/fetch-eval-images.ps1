# Downloads openly licensed (CC0 / public domain) PNG cut-outs from Wikimedia Commons for the evaluation harness.
# The alpha channels are human-made cut-outs, NOT pixel-exact ground truth: treat results as an approximate agreement score.
param([string]$OutDir = "$PSScriptRoot\images", [int]$Width = 1600)

$ErrorActionPreference = 'Stop'
$h = @{ 'User-Agent' = 'bgremover-eval/1.0 (https://github.com/itsnavin/background-remover)' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$titles = @(
    'File:Trifolium repens macro flower cutout.png',
    'File:Cactus flower extracted.png',
    'File:Nyctereutes procyonoides procyonoides Hardwicke (transparent background).png',
    'File:Plastic Flowers Transparent Background, Blue.png',
    'File:Transparent-jack-russell-puppy-dog.png'
)

$u = 'https://commons.wikimedia.org/w/api.php?action=query&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=' + $Width +
     '&iiextmetadatafilter=LicenseShortName|Artist&format=json&titles=' + [uri]::EscapeDataString(($titles -join '|'))
$j = $null
for ($try = 0; $try -lt 6 -and -not $j; $try++) {
    try { $j = Invoke-RestMethod $u -Headers $h } catch { Start-Sleep -Seconds (10 * ($try + 1)) }
}
if (-not $j) { throw 'Wikimedia API unavailable' }

$attr = @("# Evaluation image attribution`n", "Source: Wikimedia Commons. Only CC0 / public-domain files are used.`n")
$i = 0
foreach ($p in ($j.query.pages.PSObject.Properties.Value | Sort-Object { $_.title })) {
    $ii = $p.imageinfo[0]
    $lic = $ii.extmetadata.LicenseShortName.value
    if ($lic -notmatch 'CC0|Public domain') { Write-Warning "Skipping $($p.title) ($lic)"; continue }
    $i++
    $name = 'img{0:D2}.png' -f $i
    for ($try = 0; $try -lt 6; $try++) {
        try { Invoke-WebRequest $ii.thumburl -Headers $h -OutFile (Join-Path $OutDir $name) -UseBasicParsing; break }
        catch { Start-Sleep -Seconds (10 * ($try + 1)) }
    }
    $artist = ($ii.extmetadata.Artist.value -replace '<[^>]+>', '')
    $attr += "- ${name}: $($p.title) - $lic - $artist`n"
    Start-Sleep -Seconds 3
}
Set-Content -Path (Join-Path $OutDir 'ATTRIBUTION.md') -Value $attr
"Downloaded $i images to $OutDir"
