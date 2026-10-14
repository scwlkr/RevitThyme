using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RevitThyme.Codex;

namespace RevitThyme.UI;

// Ordinary WPF fields; unsupported schema branches stay declined rather than guessed.
internal static class AgentInputForm
{
    public static Func<object> Build(StackPanel panel, ServerRequest request)
    {
        var p = request.Parameters;
        if (request.Method == "item/tool/requestUserInput")
        {
            var answers = new Dictionary<string, Func<string>>();
            foreach (var question in p.GetProperty("questions").EnumerateArray())
            {
                panel.Children.Add(Label(AgentSession.Text(question, "question")));
                if (question.TryGetProperty("isSecret", out var secret) && secret.GetBoolean())
                { var password = new PasswordBox(); panel.Children.Add(password); answers[AgentSession.Text(question, "id")] = () => password.Password; continue; }
                var text = new TextBox { TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 6) }; panel.Children.Add(text);
                ComboBox? choices = null;
                if (question.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                {
                    choices = new() { ItemsSource = options.EnumerateArray().Select(o => AgentSession.Text(o, "label")).ToArray(), Margin = new(0, 3, 0, 6) }; panel.Children.Add(choices);
                    foreach (var option in options.EnumerateArray()) panel.Children.Add(Label(AgentSession.Text(option, "label") + ": " + AgentSession.Text(option, "description")));
                }
                var select = choices; answers[AgentSession.Text(question, "id")] = () => !string.IsNullOrWhiteSpace(text.Text) ? text.Text : select?.SelectedItem as string ?? "";
            }
            return () => new { answers = answers.ToDictionary(a => a.Key, a => (object)new { answers = new[] { Required(a.Value()) } }) };
        }
        if (request.Method != "mcpServer/elicitation/request") return () => new { decision = "accept" };
        panel.Children.Add(Label(AgentSession.Text(p, "message")));
        if (AgentSession.Text(p, "mode") == "url") throw new NotSupportedException("This client supports local form requests. Decline this URL request and continue in chat.");
        var schema = p.GetProperty("requestedSchema"); var fields = new Dictionary<string, Func<object?>>();
        string[] required = schema.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(v => v.GetString()!).ToArray() : [];
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            string name = property.Name; var field = property.Value; string type = AgentSession.Text(field, "type");
            panel.Children.Add(Label((AgentSession.Text(field, "title") is { Length: > 0 } title ? title : name) + (required.Contains(name) ? " *" : "")));
            if (field.TryGetProperty("description", out var description)) panel.Children.Add(Label(description.GetString() ?? ""));
            if (field.TryGetProperty("enum", out var choices))
            {
                var combo = new ComboBox { ItemsSource = choices.EnumerateArray().Select(v => v.GetString()).ToArray() }; panel.Children.Add(combo);
                fields[name] = () => required.Contains(name) ? Required(combo.SelectedItem as string ?? "") : combo.SelectedItem; continue;
            }
            if (type == "boolean")
            { var box = new CheckBox { Content = "Yes", IsThreeState = true, Margin = new(0, 3, 0, 6) }; panel.Children.Add(box); fields[name] = () => required.Contains(name) && box.IsChecked is null ? throw new InvalidOperationException("Answer " + name) : box.IsChecked; continue; }
            if (type is not ("string" or "number" or "integer")) throw new NotSupportedException("This form contains an unsupported field. Decline and continue in chat.");
            var text = new TextBox { Margin = new(0, 3, 0, 6) }; panel.Children.Add(text);
            fields[name] = () =>
            {
                if (string.IsNullOrWhiteSpace(text.Text)) return required.Contains(name) ? throw new InvalidOperationException("Answer " + name) : null;
                if (type == "string")
                {
                    if (field.TryGetProperty("minLength", out var min) && text.Text.Length < min.GetInt32() || field.TryGetProperty("maxLength", out var max) && text.Text.Length > max.GetInt32()) throw new InvalidOperationException("Check the length of " + name);
                    return text.Text;
                }
                if (!double.TryParse(text.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || type == "integer" && value != Math.Truncate(value)) throw new InvalidOperationException("Enter a valid " + type + " for " + name);
                if (field.TryGetProperty("minimum", out var low) && value < low.GetDouble() || field.TryGetProperty("maximum", out var high) && value > high.GetDouble()) throw new InvalidOperationException("Check the range of " + name);
                return value;
            };
        }
        return () => new { action = "accept", content = fields.Select(f => new { f.Key, Value = f.Value() }).Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value) };
    }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException("Answer each required question.") : value;
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 3) };
}
