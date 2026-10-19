using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using RevitThyme.Core;

namespace RevitThyme.Addin;

public sealed partial class RevitModel
{
    public ViewImageResult ExportViewImage(Guid requestId, ViewImageRequest options)
    {
        var doc = Document; host.AssertDocument(doc);
        if (doc.IsModifiable) throw new OperationException("export_blocked", "Finish the current transaction before exporting.");
        Element? element;
        try { element = doc.GetElement(options.ViewId); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { throw new OperationException("unsupported_view", "The view UniqueId is invalid for this document."); }
        if (element is not View view || view.IsTemplate || !view.CanBePrinted
            || view.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
                or ViewType.Section or ViewType.Elevation or ViewType.Detail or ViewType.DraftingView or ViewType.ThreeD or ViewType.DrawingSheet)
            || view is ViewSheet { IsPlaceholder: true })
            throw new OperationException("unsupported_view", "Use an existing printable plan, section, elevation, detail, drafting, 3D view or non-placeholder sheet UniqueId in the captured document.");
        string directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitThyme", "view-images", host.Binding.SessionId, requestId.ToString("D"));
        // CreateNew ownership: never overwrite files from a previous/unconfirmed attempt.
        Directory.CreateDirectory(directory);
        using var ownership = new FileStream(System.IO.Path.Combine(directory, "request.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var export = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews, FilePath = System.IO.Path.Combine(directory, "native"),
            HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
            ZoomType = ZoomFitType.FitToPage, FitDirection = view.Outline.Max.U - view.Outline.Min.U >= view.Outline.Max.V - view.Outline.Min.V
                ? FitDirectionType.Horizontal : FitDirectionType.Vertical,
            PixelSize = options.PixelSize, ImageResolution = ImageResolution.DPI_150
        };
        export.SetViewsAndSheets([view.Id]);
        try
        {
            doc.ExportImage(export);
            var files = Directory.GetFiles(directory, "*.png");
            if (files.Length != 1) throw new InvalidDataException("Expected exactly one native PNG.");
            string path = System.IO.Path.Combine(directory, "image.png");
            File.Move(files[0], path, overwrite: false);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is < 24 or > 8388608) throw new InvalidDataException("PNG exceeds the 8 MiB limit or is incomplete.");
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            if (frame.PixelWidth is < 1 or > 4096 || frame.PixelHeight is < 1 or > 4096) throw new InvalidDataException("Invalid exported PNG dimensions.");
            // Force pixel decoding before acknowledging completion.
            int stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            frame.CopyPixels(new byte[stride * frame.PixelHeight], stride, 0);
            stream.Position = 0;
            return new(view.UniqueId, view.Id.Value, view.Name, view.ViewType.ToString(), path,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), stream.Length, frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception error)
        {
            throw new OperationException("export_incomplete", "Native image export did not complete verification (" + error.GetType().Name + "). Partial files are retained; recover the original request ID before continuing.");
        }
    }
}
