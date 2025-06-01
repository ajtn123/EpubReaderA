using Newtonsoft.Json;

namespace EpubReaderA;

public static class JsonHelper
{
    public static string Serialize(object obj, bool indented = false)
    {
        return JsonConvert.SerializeObject(
            obj,
            indented ? Formatting.Indented : Formatting.None);
    }
}