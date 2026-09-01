namespace CandidatePortal.Api.Configuration;

public static class DotEnvLoader
{
    public static void AddDotEnvFiles(this ConfigurationManager configuration, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var values = new Dictionary<string, string?>();
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith('"') && value.EndsWith('"')) ||
                     (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }
                values[key] = value;
            }
            configuration.AddInMemoryCollection(values);
        }

        // Process-level settings always win over local files.
        configuration.AddEnvironmentVariables();
    }
}
