using System;
using System.Collections.Generic;
using System.Reflection;

namespace CleanSlate.Tests
{
    /// <summary>Marca un metodo come caso di test (equivalente minimale di [Fact]/[Test]).</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TestAttribute : Attribute
    {
        private readonly string _name;

        public TestAttribute()
        {
        }

        public TestAttribute(string name)
        {
            _name = name;
        }

        public string Name
        {
            get { return _name; }
        }
    }

    /// <summary>Fallimento di un'asserzione, con messaggio descrittivo.</summary>
    public sealed class AssertException : Exception
    {
        public AssertException(string message)
            : base(message)
        {
        }
    }

    /// <summary>asserzioni minimali per la suite.</summary>
    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertException(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new AssertException(message);
        }

        public static void Equal(object expected, object actual, string message)
        {
            if (!object.Equals(expected, actual))
                throw new AssertException(message
                    + " | atteso: " + Display(expected)
                    + " | ottenuto: " + Display(actual));
        }

        public static void NotEqual(object notExpected, object actual, string message)
        {
            if (object.Equals(notExpected, actual))
                throw new AssertException(message + " | valore non atteso ricevuto: " + Display(actual));
        }

        public static void Null(object value, string message)
        {
            if (value != null) throw new AssertException(message + " | atteso null, ottenuto: " + Display(value));
        }

        public static void NotNull(object value, string message)
        {
            if (value == null) throw new AssertException(message + " | atteso un valore non nullo.");
        }

        public static void Contains(string expectedSubstring, string actual, string message)
        {
            if (actual == null || actual.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
                throw new AssertException(message + " | non contiene: " + expectedSubstring);
        }

        public static void DoesNotContain(string forbiddenSubstring, string actual, string message)
        {
            if (actual != null && actual.IndexOf(forbiddenSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                throw new AssertException(message + " contiene il testo vietato: " + forbiddenSubstring);
        }

        public static T Throws<T>(Action action, string message) where T : Exception
        {
            if (action == null) throw new ArgumentNullException("action");
            try
            {
                action();
            }
            catch (T expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                throw new AssertException(message + " | eccezione inattesa: " + other.GetType().Name + " - " + other.Message);
            }
            throw new AssertException(message + " | nessuna eccezione generata, era attesa " + typeof(T).Name + ".");
        }

        private static string Display(object value)
        {
            if (value == null) return "null";
            return value.ToString();
        }
    }

    /// <summary>
    /// Runner della suite. Elenca i metodi marcati con [Test], li esegue in ordine
    /// alfabetico e restituisce codice di uscita non zero se anche un solo test fallisce.
    /// </summary>
    internal static class TestRunner
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("CleanSlate - suite di test");
            Console.WriteLine(new string('=', 60));

            List<MethodInfo> methods = new List<MethodInfo>();
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                MethodInfo[] candidates = types[i].GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                for (int j = 0; j < candidates.Length; j++)
                {
                    if (candidates[j].GetCustomAttributes(typeof(TestAttribute), false).Length > 0)
                        methods.Add(candidates[j]);
                }
            }

            methods.Sort(delegate(MethodInfo a, MethodInfo b)
            {
                int byType = string.CompareOrdinal(a.DeclaringType.FullName, b.DeclaringType.FullName);
                if (byType != 0) return byType;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            int passed = 0;
            int failed = 0;
            List<string> failures = new List<string>();

            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo method = methods[i];
                string name = method.DeclaringType.Name + "." + method.Name;
                object instance = null;
                try
                {
                    if (!method.IsStatic) instance = Activator.CreateInstance(method.DeclaringType);
                    method.Invoke(instance, null);
                    passed++;
                    Console.WriteLine("PASS  " + name);
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    Exception inner = tie.InnerException ?? tie;
                    Console.WriteLine("FAIL  " + name);
                    Console.WriteLine("      " + inner.Message);
                    failures.Add(name + " :: " + inner.Message);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL  " + name);
                    Console.WriteLine("      " + ex.GetType().Name + ": " + ex.Message);
                    failures.Add(name + " :: " + ex.Message);
                }
                finally
                {
                    IDisposable disposable = instance as IDisposable;
                    if (disposable != null)
                    {
                        try { disposable.Dispose(); }
                        catch (Exception) { }
                    }
                }
            }

            Console.WriteLine(new string('=', 60));
            Console.WriteLine("Totale: " + methods.Count + "  Superati: " + passed + "  Falliti: " + failed);
            if (failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Test falliti:");
                for (int i = 0; i < failures.Count; i++) Console.WriteLine("  - " + failures[i]);
            }

            return failed == 0 ? 0 : 1;
        }
    }
}
