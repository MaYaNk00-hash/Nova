using System.Reflection;

namespace Nova.Tests;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("Executing Nova.Tests suite...");
        int exitCode = SimpleTestRunner.RunAll(Assembly.GetExecutingAssembly());
        return exitCode;
    }
}
