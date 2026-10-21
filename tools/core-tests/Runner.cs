using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;

// Minimal NUnit-attribute runner so the Unity EditMode tests can run without Unity.
static class Runner
{
    static int Main(string[] args)
    {
        string filter = args.Length > 0 ? args[0] : null;
        int pass = 0, fail = 0;
        var sw = Stopwatch.StartNew();
        foreach (Type t in Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsClass && x.IsPublic))
        {
            var tests = t.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "TestAttribute")).ToList();
            if (tests.Count == 0) continue;
            if (filter != null && !t.Name.Contains(filter)) continue;
            object inst = Activator.CreateInstance(t);
            MethodInfo once = t.GetMethods().FirstOrDefault(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "OneTimeSetUpAttribute"));
            MethodInfo setup = t.GetMethods().FirstOrDefault(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "SetUpAttribute"));
            try { once?.Invoke(inst, null); }
            catch (Exception e) { Console.WriteLine($"FAIL {t.Name}.OneTimeSetUp: {Unwrap(e)}"); fail++; continue; }
            foreach (MethodInfo m in tests)
            {
                try { setup?.Invoke(inst, null); m.Invoke(inst, null); pass++; Console.WriteLine($"ok   {t.Name}.{m.Name}"); }
                catch (Exception e) { fail++; Console.WriteLine($"FAIL {t.Name}.{m.Name}: {Unwrap(e)}"); }
            }
        }
        Console.WriteLine($"\n{pass} passed, {fail} failed in {sw.ElapsedMilliseconds} ms");
        return fail == 0 ? 0 : 1;
    }

    static string Unwrap(Exception e) => (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).Message.Replace("\n", " | ");
}
