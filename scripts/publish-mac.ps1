# Builds PinList.app for Apple Silicon on Windows and zips it into publish\mac\PinList.zip.
# The same as scripts/package-mac.sh, except the signature: that needs a Mac. After unzipping on the Mac, run
#   codesign --force --deep --sign - PinList.app && xattr -cr PinList.app
# Uses only what Windows has: dotnet, System.Drawing (icon sizes) and tar.exe (a zip that keeps macOS permissions).
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$version = ([xml](Get-Content "Version.props")).Project.PropertyGroup.Version
$work = Join-Path $env:TEMP "pinlist-mac-build"
$out = Join-Path $repo "publish\mac"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
# The old zip goes first, so a failed build never leaves an outdated one that looks new
$oldZip = Join-Path $out "PinList.zip"
if (Test-Path $oldZip) { Remove-Item $oldZip }

Write-Host "Building PinList $version for macOS (Apple Silicon)..."
dotnet publish src\PinList\PinList.csproj -c Release -f net10.0 -r osx-arm64 --self-contained true -o "$work\publish"
if ($LASTEXITCODE -ne 0) { throw "The build failed." }

# ---- The app bundle ----
$app = Join-Path $work "PinList.app"
New-Item -ItemType Directory -Force "$app\Contents\MacOS", "$app\Contents\Resources" | Out-Null
Copy-Item "$work\publish\*" "$app\Contents\MacOS" -Recurse
$plist = (Get-Content "src\PinList\macOS\Info.plist" -Raw).Replace("__VERSION__", $version)
[IO.File]::WriteAllText("$app\Contents\Info.plist", $plist, (New-Object Text.UTF8Encoding $false))

# ---- The app icon: an .icns file holds PNG images in the sizes macOS asks for ----
Add-Type -AssemblyName System.Drawing
function Get-PngBytes([Drawing.Image] $source, [int] $size)
{
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}
function Get-BigEndian([int] $value)
{
    $bytes = [BitConverter]::GetBytes($value)
    [Array]::Reverse($bytes)
    return , $bytes
}
$icon = [Drawing.Image]::FromFile((Join-Path $repo "src\PinList\macOS\icon-1024.png"))
$types = [ordered]@{ icp4 = 16; icp5 = 32; icp6 = 64; ic07 = 128; ic08 = 256; ic09 = 512; ic10 = 1024 }
$entries = New-Object IO.MemoryStream
foreach ($type in $types.Keys)
{
    $png = Get-PngBytes $icon $types[$type]
    $entries.Write([Text.Encoding]::ASCII.GetBytes($type), 0, 4)
    $entries.Write((Get-BigEndian ($png.Length + 8)), 0, 4)
    $entries.Write($png, 0, $png.Length)
}
$icon.Dispose()
$icns = New-Object IO.MemoryStream
$icns.Write([Text.Encoding]::ASCII.GetBytes("icns"), 0, 4)
$icns.Write((Get-BigEndian ($entries.Length + 8)), 0, 4)
$entries.WriteTo($icns)
[IO.File]::WriteAllBytes("$app\Contents\Resources\PinList.icns", $icns.ToArray())

# ---- The zip: an mtree list tells tar each file's macOS permissions (the app and its libraries must be executable) ----
Set-Location $work
$lines = New-Object Collections.Generic.List[string]
$lines.Add("#mtree")
foreach ($item in Get-ChildItem "PinList.app" -Recurse -Force | Sort-Object FullName)
{
    $path = $item.FullName.Substring($work.Length + 1).Replace("\", "/")
    if ($item.PSIsContainer) { $lines.Add("$path type=dir mode=0755"); continue }
    $mode = if ($item.Name -in "PinList", "createdump" -or $item.Extension -eq ".dylib") { "0755" } else { "0644" }
    $lines.Add("$path type=file mode=$mode contents=$path")
}
$lines.Insert(1, "PinList.app type=dir mode=0755")
[IO.File]::WriteAllLines("$work\files.mtree", $lines)

# Zipped in the temp folder and moved when complete, so a half-written zip (a closed window) never lands in publish\mac
& "$env:SystemRoot\System32\tar.exe" --format zip -cf "$work\PinList.zip" "@files.mtree"
if ($LASTEXITCODE -ne 0) { throw "Zipping failed." }
New-Item -ItemType Directory -Force $out | Out-Null
$zip = Join-Path $out "PinList.zip"
Move-Item "$work\PinList.zip" $zip -Force
Set-Location $repo
Remove-Item $work -Recurse -Force

Write-Host ""
Write-Host "Done: $zip"
Write-Host "On the Mac, after unzipping, run in Terminal (in the folder with PinList.app):"
Write-Host "  codesign --force --deep --sign - PinList.app && xattr -cr PinList.app"
