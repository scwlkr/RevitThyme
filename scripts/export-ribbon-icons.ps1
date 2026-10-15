param([switch]$Check)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'assets/ribbon/svg'
$destination = Join-Path $root 'assets/ribbon/png'
$culture = [Globalization.CultureInfo]::InvariantCulture
$themes = @{
    light = @{ '#52606D' = '#52606D'; '#006EAF' = '#006EAF' }
    dark = @{ '#52606D' = '#D6DEE8'; '#006EAF' = '#70C5FF' }
}

function Assert-Attributes($Node, [string[]]$Allowed) {
    foreach ($attribute in $Node.Attributes) {
        if ($attribute.Name -notin $Allowed) { throw "Unsupported attribute '$($attribute.Name)' on $($Node.LocalName)." }
    }
}

function Read-Number([string]$Value) {
    $number = 0.0
    if (-not [double]::TryParse($Value, [Globalization.NumberStyles]::Float, $culture, [ref]$number) -or
        [double]::IsNaN($number) -or [double]::IsInfinity($number) -or [Math]::Abs($number) -gt 4096) {
        throw "Invalid SVG number '$Value'. Use unitless invariant-culture values."
    }
    return $number
}

function Read-AttributeNumber($Node, [string]$Name) {
    if (-not $Node.HasAttribute($Name)) { throw "Missing '$Name' on $($Node.LocalName)." }
    return Read-Number $Node.GetAttribute($Name)
}

function Read-Brush([string]$Value, $Palette) {
    if ($Value -eq 'none') { return $null }
    if (-not $Palette.ContainsKey($Value)) { throw "Unsupported SVG color '$Value'. Use the two documented palette tokens or none." }
    $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($Palette[$Value]))
    $brush.Freeze()
    return $brush
}

function Read-Geometry($Node) {
    $common = @('fill', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin')
    switch ($Node.LocalName) {
        'path' {
            Assert-Attributes $Node ($common + 'd')
            $data = $Node.GetAttribute('d')
            if ([string]::IsNullOrWhiteSpace($data) -or $data -match '[^MmLlHhVvCcSsQqTtAaZzEe0-9.,+\s-]') {
                throw 'Only SVG path commands M/L/H/V/C/S/Q/T/A/Z and numeric coordinates are supported.'
            }
            foreach ($number in [regex]::Matches($data, '[+-]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][+-]?\d+)?')) {
                Read-Number $number.Value | Out-Null
            }
            # SVG's default fill rule is nonzero; WPF's path parser otherwise defaults to even-odd.
            return [Windows.Media.Geometry]::Parse('F1 ' + $data)
        }
        'rect' {
            Assert-Attributes $Node ($common + @('x', 'y', 'width', 'height'))
            $x = Read-AttributeNumber $Node 'x'; $y = Read-AttributeNumber $Node 'y'
            $width = Read-AttributeNumber $Node 'width'; $height = Read-AttributeNumber $Node 'height'
            if ($width -le 0 -or $height -le 0) { throw 'SVG rectangle dimensions must be positive.' }
            return [Windows.Media.RectangleGeometry]::new([Windows.Rect]::new($x, $y, $width, $height))
        }
        'line' {
            Assert-Attributes $Node ($common + @('x1', 'y1', 'x2', 'y2'))
            $start = [Windows.Point]::new((Read-AttributeNumber $Node 'x1'), (Read-AttributeNumber $Node 'y1'))
            $end = [Windows.Point]::new((Read-AttributeNumber $Node 'x2'), (Read-AttributeNumber $Node 'y2'))
            return [Windows.Media.LineGeometry]::new($start, $end)
        }
        'circle' {
            Assert-Attributes $Node ($common + @('cx', 'cy', 'r'))
            $center = [Windows.Point]::new((Read-AttributeNumber $Node 'cx'), (Read-AttributeNumber $Node 'cy'))
            $radius = Read-AttributeNumber $Node 'r'
            if ($radius -le 0) { throw 'SVG circle radius must be positive.' }
            return [Windows.Media.EllipseGeometry]::new($center, $radius, $radius)
        }
        'polyline' {
            Assert-Attributes $Node ($common + 'points')
            $values = @($Node.GetAttribute('points').Trim() -split '[,\s]+' | ForEach-Object { Read-Number $_ })
            if ($values.Count -lt 4 -or $values.Count % 2 -ne 0) { throw 'SVG polyline requires coordinate pairs.' }
            $figure = [Windows.Media.PathFigure]::new()
            $figure.StartPoint = [Windows.Point]::new($values[0], $values[1])
            for ($index = 2; $index -lt $values.Count; $index += 2) {
                $figure.Segments.Add([Windows.Media.LineSegment]::new([Windows.Point]::new($values[$index], $values[$index + 1]), $true))
            }
            $geometry = [Windows.Media.PathGeometry]::new(); $geometry.Figures.Add($figure)
            return $geometry
        }
        default { throw "Unsupported SVG element '$($Node.LocalName)'." }
    }
}

function Read-Icon($File, [int]$Size) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null; $settings.MaxCharactersInDocument = 65536
    $reader = [Xml.XmlReader]::Create($File.FullName, $settings)
    try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader) }
    finally { $reader.Dispose() }
    $svg = $document.DocumentElement
    if ($svg.LocalName -ne 'svg' -or $svg.NamespaceURI -ne 'http://www.w3.org/2000/svg') { throw 'Expected an SVG root.' }
    Assert-Attributes $svg @('xmlns', 'width', 'height', 'viewBox')
    if ((Read-AttributeNumber $svg 'width') -ne $Size -or (Read-AttributeNumber $svg 'height') -ne $Size -or
        $svg.GetAttribute('viewBox') -cne "0 0 $Size $Size") { throw "SVG dimensions must match its $Size px optical master." }
    foreach ($child in $document.ChildNodes) {
        if ($child -ne $svg -and $child.NodeType -notin @([Xml.XmlNodeType]::XmlDeclaration, [Xml.XmlNodeType]::Whitespace)) {
            throw 'Only an optional XML declaration and the SVG root are permitted.'
        }
    }
    $shapes = [Collections.Generic.List[object]]::new()
    foreach ($node in $svg.ChildNodes) {
        if ($node.NodeType -eq [Xml.XmlNodeType]::Whitespace) { continue }
        if ($node.NodeType -ne [Xml.XmlNodeType]::Element -or $node.NamespaceURI -ne $svg.NamespaceURI) { throw 'Unsupported SVG content.' }
        if ($node.LocalName -in @('title', 'desc')) {
            Assert-Attributes $node @()
            if (@($node.ChildNodes | Where-Object { $_.NodeType -ne [Xml.XmlNodeType]::Text }).Count) { throw 'SVG metadata must be plain text.' }
            continue
        }
        $geometry = Read-Geometry $node
        if (-not $node.HasAttribute('fill') -or -not $node.HasAttribute('stroke')) { throw 'Each shape requires explicit fill and stroke.' }
        $cap = if ($node.HasAttribute('stroke-linecap')) { $node.GetAttribute('stroke-linecap') } else { 'butt' }
        $join = if ($node.HasAttribute('stroke-linejoin')) { $node.GetAttribute('stroke-linejoin') } else { 'miter' }
        if ($cap -notin @('butt', 'square', 'round') -or $join -notin @('miter', 'bevel', 'round')) { throw 'Unsupported stroke cap or join.' }
        $width = if ($node.GetAttribute('stroke') -eq 'none') { 0 } else { Read-AttributeNumber $node 'stroke-width' }
        if ($node.GetAttribute('stroke') -ne 'none' -and $width -le 0) { throw 'Stroke width must be positive.' }
        $shapes.Add(@{ Geometry = $geometry; Fill = $node.GetAttribute('fill'); Stroke = $node.GetAttribute('stroke'); Width = $width; Cap = $cap; Join = $join })
    }
    if (-not $shapes.Count) { throw 'SVG must contain at least one supported shape.' }
    return ,$shapes
}

function Render-Icon($Shapes, [int]$Size, [int]$Scale, $Palette) {
    $visual = [Windows.Media.DrawingVisual]::new(); $drawing = $visual.RenderOpen()
    try {
        foreach ($shape in $Shapes) {
            $fill = Read-Brush $shape.Fill $Palette; $stroke = Read-Brush $shape.Stroke $Palette
            $pen = $null
            if ($stroke) {
                $pen = [Windows.Media.Pen]::new($stroke, $shape.Width)
                $pen.StartLineCap = $pen.EndLineCap = switch ($shape.Cap) { 'round' { 'Round' } 'square' { 'Square' } default { 'Flat' } }
                $pen.LineJoin = $shape.Join; $pen.MiterLimit = 4
            }
            $bounds = $shape.Geometry.GetRenderBounds($pen)
            if ($bounds.Left -lt 0 -or $bounds.Top -lt 0 -or $bounds.Right -gt $Size -or $bounds.Bottom -gt $Size) { throw 'SVG drawing extends outside its canvas.' }
            $drawing.DrawGeometry($fill, $pen, $shape.Geometry)
        }
    }
    finally { $drawing.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size * $Scale, $Size * $Scale, 96 * $Scale, 96 * $Scale, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

function Assert-Png([byte[]]$Bytes, [int]$Pixels, [int]$Dpi) {
    $stream = [IO.MemoryStream]::new($Bytes, $false)
    try {
        $decoder = [Windows.Media.Imaging.PngBitmapDecoder]::new($stream, [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
        if ($frame.PixelWidth -ne $Pixels -or $frame.PixelHeight -ne $Pixels -or [Math]::Abs($frame.DpiX - $Dpi) -gt 0.02 -or [Math]::Abs($frame.DpiY - $Dpi) -gt 0.02) {
            throw 'PNG pixel size or logical DPI is incorrect.'
        }
    }
    finally { $stream.Dispose() }
}

$files = @(Get-ChildItem -LiteralPath $source -File | Sort-Object Name)
if (-not $files.Count) { throw 'No SVG masters found.' }
$expected = @{}; $sizes = @{}
foreach ($file in $files) {
    if ($file.Name -cnotmatch '^([a-z][a-z0-9-]+)\.(16|32)\.svg$') { throw "Unexpected SVG master '$($file.Name)'." }
    $id = $Matches[1]; $size = [int]$Matches[2]
    if (-not $sizes.ContainsKey($id)) { $sizes[$id] = @() }
    $sizes[$id] += $size
    $shapes = Read-Icon $file $size
    foreach ($theme in @('light', 'dark')) {
        foreach ($scale in @(1, 2)) {
            $suffix = if ($scale -eq 2) { '@2x' } else { '' }
            $name = "$id.$theme.$size$suffix.png"
            $bytes = Render-Icon $shapes $size $scale $themes[$theme]
            Assert-Png $bytes ($size * $scale) (96 * $scale)
            $expected.Add($name, $bytes)
        }
    }
}
foreach ($id in $sizes.Keys) { if ($sizes[$id].Count -ne 2) { throw "Both 16 px and 32 px SVG masters are required for $id." } }
if (Test-Path -LiteralPath $destination) {
    foreach ($file in Get-ChildItem -LiteralPath $destination -File) {
        if (-not $expected.ContainsKey($file.Name)) { throw "Stale or unexpected output '$($file.FullName)'. Remove it deliberately." }
    }
}
if (-not $Check) { New-Item -ItemType Directory -Path $destination -Force | Out-Null }
foreach ($name in ($expected.Keys | Sort-Object)) {
    $path = Join-Path $destination $name
    if ($Check) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing generated icon '$name'. Run scripts/export-ribbon-icons.ps1." }
        $actual = [IO.File]::ReadAllBytes($path)
        if ([Convert]::ToBase64String($actual) -cne [Convert]::ToBase64String($expected[$name])) { throw "Generated icon '$name' differs from its SVG master. Regenerate icons." }
    }
    else { [IO.File]::WriteAllBytes($path, $expected[$name]) }
}
$action = if ($Check) { 'Checked' } else { 'Exported' }
Write-Output "$action $($files.Count) optical SVG masters and $($expected.Count) transparent light/dark PNGs (96/192 DPI)."
