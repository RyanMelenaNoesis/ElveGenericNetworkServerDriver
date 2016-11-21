using CodecoreTechnologies.Elve.DriverFramework;
using System.Text;

namespace System.IO
{
    public static class StreamReaderExtensions
    {
        public static string ReadToDelimiter(this StreamReader self, string delimeter, ILogger logger)
        {
            StringBuilder currentLine = new StringBuilder();
            int i;
            char c;
            while ((i = self.Read()) >= 0)
            {
                c = (char)i;
                currentLine.Append(c);
                if (currentLine.ToString().EndsWith(delimeter))
                {
                    currentLine.Remove(currentLine.Length - delimeter.Length, delimeter.Length);
                    break;
                }
                else if(currentLine.Length >= 256)
                {
                    logger.Warning((object)("Received max characters (256) without delimiter.  Breaking received string at 256 characters. [" + currentLine.ToString() + "]"));
                    break;
                }
            }

            return currentLine.ToString();
        }
    }
}