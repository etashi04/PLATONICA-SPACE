param(
    [Parameter(Mandatory = $true)][string]$CurrentRawDirectory,
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [Parameter(Mandatory = $true)][string]$SourcePath
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$sourceCode = [System.IO.File]::ReadAllText($SourcePath, $utf8)
$supplemental = @{}
$pattern = 'new KeyValuePair<string, string>\("((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"\)'
foreach ($match in [regex]::Matches($sourceCode, $pattern)) {
    $source = [regex]::Unescape($match.Groups[1].Value)
    $target = [regex]::Unescape($match.Groups[2].Value)
    $supplemental[$source] = $target
}

$files = @(
    'Memory00_00__5742491962506870961.txt',
    'Memory01_00__-2063483842202675069.txt',
    'Memory02_00__485205599610408592.txt',
    'Memory03_00__1620152860337240270.txt',
    'Memory04_00__-4653386823386038896.txt'
)

$results = @{}
foreach ($file in $files) {
    $currentPath = Join-Path $CurrentRawDirectory $file
    $patchedPath = Join-Path $DataDirectory $file
    $oldLines = [System.IO.File]::ReadAllLines($patchedPath, $utf8)
    $byEnglish = @{}
    foreach ($line in $oldLines) {
        $columns = $line -split "`t", 3
        if ($columns.Count -eq 3 -and -not [string]::IsNullOrEmpty($columns[2])) {
            if (-not $byEnglish.ContainsKey($columns[2])) { $byEnglish[$columns[2]] = $columns[1] }
        }
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    $translatedRows = 0
    $output = foreach ($line in [System.IO.File]::ReadAllLines($currentPath, $utf8)) {
        $columns = $line -split "`t", 3
        if ($columns.Count -ne 3) { $line; continue }
        $target = $null
        if ($byEnglish.ContainsKey($columns[2])) { $target = $byEnglish[$columns[2]] }
        elseif ($supplemental.ContainsKey($columns[1])) { $target = $supplemental[$columns[1]] }
        elseif ($columns[1] -notmatch '[\p{IsHiragana}\p{IsKatakana}]') { $target = $columns[1] }
        else { $missing.Add($columns[1]); $line; continue }
        if ($target -ne $columns[1]) { $translatedRows++ }
        $columns[0] + "`t" + $target + "`t" + $columns[2]
    }
    if ($missing.Count -gt 0) {
        throw "$file has $($missing.Count) untranslated current rows:`n$($missing -join "`n")"
    }
    [System.IO.File]::WriteAllText($patchedPath, (($output -join "`n") + "`n"), $utf8)
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $currentPath).Hash
    $patchedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $patchedPath).Hash
    $results[$file] = [pscustomobject]@{
        SourceHash = $sourceHash
        PatchedHash = $patchedHash
        TranslatedRows = $translatedRows
    }
}

$manifestPath = Join-Path $DataDirectory 'runtime-text-patch-manifest.tsv'
$manifest = [System.IO.File]::ReadAllLines($manifestPath, $utf8)
for ($index = 1; $index -lt $manifest.Length; $index++) {
    $columns = $manifest[$index] -split "`t"
    if ($columns.Count -ne 6 -or -not $results.ContainsKey($columns[5])) { continue }
    $result = $results[$columns[5]]
    $columns[2] = $result.SourceHash
    $columns[3] = $result.PatchedHash
    $columns[4] = [string]$result.TranslatedRows
    $manifest[$index] = $columns -join "`t"
}
[System.IO.File]::WriteAllText($manifestPath, (($manifest -join "`n") + "`n"), $utf8)
$results.GetEnumerator() | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{
        File = $_.Name
        SourceHash = $_.Value.SourceHash
        PatchedHash = $_.Value.PatchedHash
        TranslatedRows = $_.Value.TranslatedRows
    }
}
