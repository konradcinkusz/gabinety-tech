namespace Gabinety.DemoSeeder;

/// <summary>Where progress goes. Lines carry demo keys and counts only: never a token, a key, a password or a response body.</summary>
public interface ISeedLog
{
    void Info(string line);
}

public sealed class ConsoleSeedLog : ISeedLog
{
    public void Info(string line) => Console.Out.WriteLine(line);
}

/// <summary>The failure the seeder reports. Its message is built from status codes, error codes and demo keys only.</summary>
public sealed class SeedException(string message) : Exception(message);
