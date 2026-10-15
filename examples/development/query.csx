return new {
    document = doc.Title,
    viewId = doc.ActiveView.Id.Value,
    viewName = doc.ActiveView.Name
};
