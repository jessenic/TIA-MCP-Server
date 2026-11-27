using System.IO;
using System.Text;

namespace TiaOpennessMcp.Tia
{
    /// <summary>
    /// Writes external source files for PlcExternalSourceComposition.CreateFromFile. TIA Portal V21 reads
    /// external sources as ANSI, UTF-8 with BOM, UTF-16/32 with BOM or UCS2 ("Basics of using external
    /// source files"); BOM-less UTF-8 is read as ANSI and breaks non-ASCII titles and comments.
    /// Kept free of Siemens types so it can be tested offline.
    /// </summary>
    public static class ExternalSourceFile
    {
        public static void Write(string path, string code)
        {
            File.WriteAllText(path, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }
}
