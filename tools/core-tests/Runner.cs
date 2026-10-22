using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;

// Minimal NUnit-attribute runner so the Unity EditMode tests can run without Unity.
// Supports [Test], [TestCase], [SetUp], [TearDown], [OneTimeSetUp] and [OneTimeTearDown]. Anything else that changes how a
// test runs (TestCaseSource, Values, Range, ...) is refused loudly rather than skipped, so the fast loop never reports a
// pass for a test it did not really run.
static class Runner
{
    static readonly string[] Unsupported = { "TestCaseSourceAttribute", "ValuesAttribute", "RangeAttribute", "ValueSourceAttribute", "CombinatorialAttribute", "PairwiseAttribute", "SequentialAttribute", "RepeatAttribute", "RetryAttribute" };

    static int Main(string[] args)
    {
        string filter = args.Length > 0 ? args[0] : null;
        int pass = 0, fail = 0;
        var sw = Stopwatch.StartNew();
        foreach (Type t in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.IsPublic))
        {
            List<MethodInfo> tests = t.GetMethods().Where(m => Has(m, "TestAttribute") || Has(m, "TestCaseAttribute")).ToList();
            if (tests.Count == 0) continue;
            if (filter != null && !t.Name.Contains(filter)) continue;

            object inst = Activator.CreateInstance(t);
            MethodInfo once = Find(t, "OneTimeSetUpAttribute");
            MethodInfo onceDown = Find(t, "OneTimeTearDownAttribute");
            MethodInfo setup = Find(t, "SetUpAttribute");
            MethodInfo teardown = Find(t, "TearDownAttribute");
            try { once?.Invoke(inst, null); }
            catch (Exception e) { Console.WriteLine($"FAIL {t.Name}.OneTimeSetUp: {Unwrap(e)}"); fail++; continue; }

            foreach (MethodInfo m in tests)
            {
                foreach (string bad in Unsupported.Where(u => Has(m, u)))
                {
                    fail++;
                    Console.WriteLine($"FAIL {t.Name}.{m.Name}: the fast runner does not support {bad}; run it in Unity or teach Runner.cs");
                }
                if (Unsupported.Any(u => Has(m, u))) continue;

                foreach ((string name, object[] callArgs) in Cases(m))
                {
                    try
                    {
                        setup?.Invoke(inst, null);
                        m.Invoke(inst, callArgs);
                        pass++;
                        Console.WriteLine($"ok   {t.Name}.{name}");
                    }
                    catch (Exception e) { fail++; Console.WriteLine($"FAIL {t.Name}.{name}: {Unwrap(e)}"); }
                    finally
                    {
                        try { teardown?.Invoke(inst, null); }
                        catch (Exception e) { fail++; Console.WriteLine($"FAIL {t.Name}.{name} (TearDown): {Unwrap(e)}"); }
                    }
                }
            }

            try { onceDown?.Invoke(inst, null); }
            catch (Exception e) { Console.WriteLine($"FAIL {t.Name}.OneTimeTearDown: {Unwrap(e)}"); fail++; }
        }
        Console.WriteLine($"\n{pass} passed, {fail} failed in {sw.ElapsedMilliseconds} ms");
        return fail == 0 ? 0 : 1;
    }

    static bool Has(MemberInfo m, string attribute) => m.GetCustomAttributes().Any(a => a.GetType().Name == attribute);

    static MethodInfo Find(Type t, string attribute) => t.GetMethods().FirstOrDefault(m => Has(m, attribute));

    /// <summary>One call per [TestCase]; a plain [Test] is one call with no arguments.</summary>
    static IEnumerable<(string name, object[] args)> Cases(MethodInfo m)
    {
        if (Has(m, "TestAttribute")) yield return (m.Name, null);
        foreach (Attribute a in m.GetCustomAttributes().Where(a => a.GetType().Name == "TestCaseAttribute"))
        {
            var arguments = (object[])a.GetType().GetProperty("Arguments").GetValue(a);
            yield return ($"{m.Name}({string.Join(", ", arguments.Select(Show))})", arguments);
        }
    }

    static string Show(object o) => o == null ? "null" : o is string s ? "\"" + (s.Length > 40 ? s.Substring(0, 40) + "..." : s).Replace("\n", "\\n") + "\"" : o.ToString();

    static string Unwrap(Exception e) => (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).Message.Replace("\n", " | ");
}
