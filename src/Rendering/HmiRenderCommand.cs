using System;
using System.Globalization;
using System.Text.Json;

namespace TiaOpennessMcp.Rendering
{
    internal static class HmiRenderCommand
    {
        public static int Run(string[] args)
        {
            try
            {
                string? input = null, output = null, screen = null, language = null;
                double scale = 2;
                for (int i = 0; i < args.Length; i++)
                {
                    string option = args[i];
                    if (i + 1 == args.Length) throw new ArgumentException($"Missing value for '{option}'.");
                    string value = args[++i];
                    switch (option)
                    {
                        case "--render-hmi": input = value; break;
                        case "--output": output = value; break;
                        case "--screen": screen = value; break;
                        case "--language": language = value; break;
                        case "--scale": scale = double.Parse(value, CultureInfo.InvariantCulture); break;
                        default: throw new ArgumentException($"Unknown render option '{option}'.");
                    }
                }
                if (input == null || output == null)
                    throw new ArgumentException("--render-hmi INPUT_DIRECTORY --output OUTPUT_DIRECTORY are required.");
                Console.WriteLine(JsonSerializer.Serialize(HmiJsonRenderer.Render(input, output, screen, language, scale)));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[HmiRenderer] " + error);
                return 1;
            }
        }
    }
}
