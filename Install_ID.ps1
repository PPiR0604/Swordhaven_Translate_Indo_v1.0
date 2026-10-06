param([Parameter(Mandatory = $true)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GameDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'MANIFEST.json') -Raw | ConvertFrom-Json
if ($manifest.package_kind -ne 'manual') { throw 'Gunakan installer ini dari paket manual.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Swordhaven.exe') -PathType Leaf)) { throw 'Swordhaven.exe tidak ditemukan di folder target.' }
if (Get-Process -Name Swordhaven -ErrorAction SilentlyContinue) { throw 'Tutup game Swordhaven sebelum memasang mod.' }
$dllPath = Join-Path $gameRoot 'Swordhaven_Data\Managed\AtomGame.dll'
$installedHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($installedHash -notin $manifest.accepted_installed_dll_sha256) { throw 'Hash DLL game tidak cocok dengan build yang didukung. Instalasi dibatalkan; jangan menimpa DLL versi game lain.' }
$entries = @($manifest.files.PSObject.Properties | Where-Object { $_.Name -like 'Localization/*' -or $_.Name -like 'Swordhaven_Data/*' })
if ($entries.Count -ne 59) { throw 'Manifest payload tidak lengkap.' }
$prepared = @()
foreach ($entry in $entries) {
    $relative = $entry.Name.Replace('/', '\')
    $source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $relative))
    if (-not $source.StartsWith($PSScriptRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($gameRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path payload keluar dari folder yang ditentukan.' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw ('Berkas paket rusak: ' + $entry.Name) }
    $prepared += [pscustomobject]@{ Relative = $relative; Source = $source; Target = $target; Existed = (Test-Path -LiteralPath $target -PathType Leaf) }
}
$backupRoot = Join-Path $gameRoot ('.Swordhaven_ID_Backups\' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
foreach ($file in $prepared) {
    if ($file.Existed) {
        $backupPath = Join-Path $backupRoot $file.Relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($backupPath)) | Out-Null
        Copy-Item -LiteralPath $file.Target -Destination $backupPath
    }
}
$prepared | Select-Object Relative,Existed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupRoot 'backup-index.json') -Encoding utf8
$written = @()
try {
    foreach ($file in $prepared) {
        $written += $file
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($file.Target)) | Out-Null
        Copy-Item -LiteralPath $file.Source -Destination $file.Target -Force
        if ((Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash) { throw ('Verifikasi gagal: ' + $file.Relative) }
    }
} catch {
    foreach ($file in $written) {
        if ($file.Existed) { Copy-Item -LiteralPath (Join-Path $backupRoot $file.Relative) -Destination $file.Target -Force }
        elseif (Test-Path -LiteralPath $file.Target -PathType Leaf) { Remove-Item -LiteralPath $file.Target }
    }
    throw
}
Write-Output ('Mod Bahasa Indonesia v' + $manifest.mod_version + ' berhasil dipasang (' + $manifest.variant + ').')
Write-Output ('Backup berkas sebelumnya: ' + $backupRoot)
Write-Output 'Pilih Bahasa Indonesia di pengaturan jika preferensi bahasa lama masih aktif. Save tidak diubah.'
