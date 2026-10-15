#nullable enable
// Post exactly one native Undo after this read-only callback returns. Record the actual Undo event.
var fixturePath = doc.PathName;
var evidence = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixturePath)!, "native-undo-event.json");
if (System.IO.File.Exists(evidence)) throw new Exception("Undo evidence already exists; do not post another Undo.");
EventHandler<Autodesk.Revit.DB.Events.DocumentChangedEventArgs>? handler = null;
handler = (sender, args) =>
{
    if (args.GetDocument().PathName != fixturePath || args.Operation != Autodesk.Revit.DB.Events.UndoOperation.TransactionUndone) return;
    uiapp.Application.DocumentChanged -= handler;
    var value = new {
        operation = args.Operation.ToString(), transactions = args.GetTransactionNames().ToArray(),
        added = args.GetAddedElementIds().Select(x => x.Value).ToArray(),
        modified = args.GetModifiedElementIds().Select(x => x.Value).ToArray(),
        deleted = args.GetDeletedElementIds().Select(x => x.Value).ToArray(),
        processId = Environment.ProcessId, fixture = fixturePath
    };
    System.IO.File.WriteAllText(evidence, System.Text.Json.JsonSerializer.Serialize(value));
};
uiapp.Application.DocumentChanged += handler;
try { uiapp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.Undo)); }
catch { uiapp.Application.DocumentChanged -= handler; throw; }
return new { postedExactlyOneUndo = true, evidence, fixture = fixturePath, processId = Environment.ProcessId };
