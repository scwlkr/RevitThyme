var view = doc.ActiveView;
var before = view.Name;
view.Name = before + " - Dev test";
return new { viewId = view.Id.Value, before, after = view.Name };
