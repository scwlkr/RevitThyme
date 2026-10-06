namespace RevitThyme.Native;
public sealed class Rejection(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
