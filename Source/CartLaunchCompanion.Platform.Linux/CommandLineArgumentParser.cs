namespace CartLaunchCompanion.Platform.Linux;

internal static class CommandLineArgumentParser
{
    public static IReadOnlyList<string> Parse(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return [];

        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrent(arguments, current);
                continue;
            }

            current.Append(character);
        }

        AddCurrent(arguments, current);
        return arguments;
    }

    private static void AddCurrent(
        List<string> arguments,
        System.Text.StringBuilder current)
    {
        if (current.Length == 0)
            return;

        arguments.Add(current.ToString());
        current.Clear();
    }
}
