# RevitThyme artwork

The supplied RevitThyme wordmark is preserved in revitthyme-logo.svg. Ribbon artwork uses its teal building forms and green thyme motif: a checked building for Suite Status, a leaf gear for Tool Settings and a timber house for Inspect TimberFold.

Editable SVG sources have a transparent 64 x 64 canvas. Their exported RGBA PNGs live in each pyRevit pushbutton as icon.png and icon.dark.png. pyRevit chooses the dark variant for a dark Revit theme and scales images for the ribbon. Keep both variants in packaging/release-files.json when adding a button.

Base colors: teal #174445 / #306F66, leaf #4F7C4D, timber #C8944D / #E2BC82 / #B47939, paper #F0EEE4. Dark exports replace #174445 with #B8DDD4, #306F66 with #265B55 and #4F7C4D with #83B36F. Use an SVG editor or renderer to export transparent 64 x 64 PNGs; no renderer is needed to install or package the committed assets.

These are project-owned assets covered by the repository license. Retain the vector sources when revising the raster exports. The original supplied file outside this directory is left untouched.
