using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AffineGameLights
{
    internal static class BrightnessConfig
    {
        internal const int DefaultBrightness = 128;
        internal const string DefaultContents = "[lighting]\r\nbrightness=128\r\n";

        internal static int Load(string path, Action<string> log)
        {
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultContents, new UTF8Encoding(false));
                    log("CONFIG_CREATED path=" + path + " brightness=" + DefaultBrightness);
                    return DefaultBrightness;
                }

                bool inLighting = false;
                string value = null;
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inLighting = string.Equals(line.Substring(1, line.Length - 2).Trim(), "lighting", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inLighting) continue;
                    int equals = line.IndexOf('=');
                    if (equals < 0) continue;
                    if (string.Equals(line.Substring(0, equals).Trim(), "brightness", StringComparison.OrdinalIgnoreCase))
                        value = line.Substring(equals + 1).Trim();
                }

                int brightness;
                if (value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out brightness)
                    && brightness >= 0 && brightness <= 255)
                {
                    log("CONFIG_LOADED path=" + path + " brightness=" + brightness);
                    return brightness;
                }
                log("CONFIG_INVALID path=" + path + " brightness=" + (value ?? "<missing>")
                    + " using_default=" + DefaultBrightness);
            }
            catch (Exception ex)
            {
                log("CONFIG_ERROR path=" + path + " error=" + ex.GetType().Name
                    + " using_default=" + DefaultBrightness);
            }
            return DefaultBrightness;
        }
    }
}
