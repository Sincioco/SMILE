using System;

namespace SMILE
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var interpreter = new SmileInterpreter();

            Console.WriteLine("SMILE BASIC Interpreter");
            Console.WriteLine("Type BASIC-style commands.");
            Console.WriteLine("Example: Print \"Hello World\"");
            Console.WriteLine("Type Exit to quit.");
            Console.WriteLine();

            while (true)
            {
                Console.Write("> ");

                string? line = Console.ReadLine();

                if (line == null)
                {
                    return 0;
                }

                line = line.Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                try
                {
                    string csharpCode = interpreter.TranslateToCSharp(line);

                    Console.WriteLine("Translated C#:");
                    Console.WriteLine(csharpCode);

                    Console.WriteLine("Output:");
                    interpreter.Execute(line);
                }
                catch (SmileInterpreterException ex)
                {
                    Console.WriteLine("SMILE Error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unexpected Error: " + ex.Message);
                }

                Console.WriteLine();
            }
        }
    }
}
