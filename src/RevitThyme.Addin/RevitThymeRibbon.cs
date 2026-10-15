using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitThyme.Addin;

internal sealed class RevitThymeRibbon : IDisposable
{
    private readonly UIControlledApplication application;
    private readonly List<(PushButton Button, string Icon)> buttons = [];
    private readonly Dictionary<string, BitmapImage> images = [];

    public RevitThymeRibbon(UIControlledApplication application)
    {
        this.application = application;
        try { application.CreateRibbonTab("RevitThyme"); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* Another owned component created the tab. */ }
        var views = application.CreateRibbonPanel("RevitThyme", "Views");
        Add<ViewRangeCommand, ViewRangeAvailability>(views, "RevitThyme.Native.ViewRange", "View\nRange", "view-range",
            "Opens a section preview and editor for the active plan's view range.",
            "Activate a floor, ceiling or structural plan. Review the top, cut plane, bottom and view depth before applying changes. " +
            "View templates can restrict editing. Apply creates one Revit Undo item.", "NATIVE-UTILITIES.md#view-range");
        Add<InspectCommand, DocumentAvailability>(views, "RevitThyme.InspectView", "Inspect\nView", "inspect-view",
            "Shows the active document and view, with an option to rename the view.",
            "Open a document and activate the view to inspect. Review its name, type and edit readiness. " +
            "Opening the window does not change the model. Applying a new name creates one Revit Undo item.", "FOUNDATION.md");

        var annotation = application.CreateRibbonPanel("RevitThyme", "Annotation");
        Add<RoomDimensionsCommand, RoomDimensionsAvailability>(annotation, "RevitThyme.Native.RoomDimensions", "Room\nDimensions", "room-dimensions",
            "Previews and creates width and depth dimensions for a rectangular room.",
            "Activate a project floor plan with an enclosed rectangular room. Select a room and review its finish-face dimensions, " +
            "inset and tag clearance. Create adds two associative dimensions in one Revit Undo item.", "NATIVE-UTILITIES.md#room-dimensions");

        var product = application.CreateRibbonPanel("RevitThyme", "RevitThyme");
        Add<AgentCommand, DocumentAvailability>(product, "RevitThyme.Agent", "Open\nAgent", "open-agent",
            "Opens the RevitThyme Agent panel for working with Codex.",
            "Open a document, then open the dockable panel and connect Codex or sign in there. Opening the panel does not start a chat or change the model. " +
            "The Agent uses this Revit session when connected.", "AGENT.md");
        product.AddSlideOut();
        Add<UpdateCommand, ApplicationAvailability>(product, "RevitThyme.Updates", "Check for\nUpdates", "check-updates",
            "Checks for a newer stable RevitThyme release.",
            "No open model is required. Review the available version and download a verified installer. " +
            "Close Revit before installing. Checking or downloading does not change the model.", "UPDATES.md");
        UpdateImages();
        application.ThemeChanged += ThemeChanged;
    }

    private void Add<TCommand, TAvailability>(RibbonPanel panel, string id, string label, string icon,
        string tooltip, string description, string help)
        where TCommand : IExternalCommand where TAvailability : IExternalCommandAvailability
    {
        var data = new PushButtonData(id, label, typeof(RevitThymeRibbon).Assembly.Location, typeof(TCommand).FullName!)
        {
            ToolTip = tooltip,
            LongDescription = description,
            AvailabilityClassName = typeof(TAvailability).FullName!
        };
        data.SetContextualHelp(new(ContextualHelpType.Url, "https://github.com/scwlkr/RevitThyme/blob/master/docs/" + help));
        buttons.Add(((PushButton)panel.AddItem(data), icon));
    }

    private void ThemeChanged(object? sender, ThemeChangedEventArgs args) => UpdateImages();
    private void UpdateImages()
    {
        string theme = UIThemeManager.CurrentTheme == UITheme.Dark ? "dark" : "light";
        // PNG DPI metadata preserves 16/32 logical units on high-resolution displays.
        string scale = GetDpiForWindow(application.MainWindowHandle) > 96 ? "@2x" : "";
        foreach (var (button, icon) in buttons)
        {
            button.Image = Image($"{icon}.{theme}.16{scale}.png");
            button.LargeImage = Image($"{icon}.{theme}.32{scale}.png");
        }
    }

    private BitmapImage Image(string name)
    {
        if (images.TryGetValue(name, out var image)) return image;
        using var stream = typeof(RevitThymeRibbon).Assembly.GetManifestResourceStream("RevitThyme.Ribbon." + name)
            ?? throw new InvalidDataException("Missing embedded ribbon icon: " + name);
        image = new(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); images.Add(name, image); return image;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    public void Dispose() => application.ThemeChanged -= ThemeChanged;
}

public sealed class ApplicationAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}

public sealed class DocumentAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => applicationData.ActiveUIDocument is not null;
}

public sealed class ViewRangeAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.ActiveView is ViewPlan { IsTemplate: false } view
        && view.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan;
}

public sealed class RoomDimensionsAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document is { IsFamilyDocument: false, ActiveView: ViewPlan { IsTemplate: false, ViewType: ViewType.FloorPlan } };
}
