namespace AffectedTestsProbe;

internal static class CommandArgs
{
    public static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    public static List<string> Values(string[] args, string name)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
    }

    public static bool Has(string[] args, string name)
    {
        foreach (var arg in args)
        {
            if (arg == name)
            {
                return true;
            }
        }

        return false;
    }

    public static int Integer(string[] args, string name, int fallback)
    {
        var text = Value(args, name);
        return text is not null && int.TryParse(text, out var value) ? value : fallback;
    }
}
