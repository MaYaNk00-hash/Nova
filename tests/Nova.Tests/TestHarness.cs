using System.Diagnostics;
using System.Reflection;

namespace Nova.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute
{
    public string? Description { get; }
    public FactAttribute(string? description = null) => Description = description;
}

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Assert.True failed: Expected true, got false.");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
            throw new InvalidOperationException(message ?? "Assert.False failed: Expected false, got true.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Assert.Equal failed: Expected '{expected}', got '{actual}'.");
    }

    public static void NotNull(object? obj, string? message = null)
    {
        if (obj is null)
            throw new InvalidOperationException(message ?? "Assert.NotNull failed: Expected non-null value.");
    }

    public static void Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(message ?? $"Assert.Throws failed: Expected exception {typeof(TException).Name}, but caught {ex.GetType().Name}: {ex.Message}");
        }

        throw new InvalidOperationException(message ?? $"Assert.Throws failed: Expected exception {typeof(TException).Name}, but no exception was thrown.");
    }
}

public static class SimpleTestRunner
{
    public static int RunAll(Assembly assembly)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("           NOVA TEST HARNESS                     ");
        Console.WriteLine("=================================================");

        var testMethods = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
            .ToList();

        int passed = 0;
        int failed = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var method in testMethods)
        {
            var fact = method.GetCustomAttribute<FactAttribute>()!;
            var testName = $"{method.DeclaringType?.Name}.{method.Name}";
            if (!string.IsNullOrWhiteSpace(fact.Description))
            {
                testName += $" ({fact.Description})";
            }

            Console.Write($"[TEST] {testName} ... ");

            try
            {
                object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, null);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASSED");
                Console.ResetColor();
                passed++;
            }
            catch (TargetInvocationException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED");
                Console.ResetColor();
                Console.WriteLine($"       Error: {ex.InnerException?.Message ?? ex.Message}");
                failed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED");
                Console.ResetColor();
                Console.WriteLine($"       Error: {ex.Message}");
                failed++;
            }
        }

        stopwatch.Stop();
        Console.WriteLine("-------------------------------------------------");
        Console.WriteLine($"Total tests: {passed + failed} | Passed: {passed} | Failed: {failed} | Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine("=================================================");

        return failed == 0 ? 0 : 1;
    }
}

