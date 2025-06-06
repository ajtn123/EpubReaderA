namespace EpubReaderA;

public static class Constants
{
    public static string VirtualHost => "epub.temp";
    public static string VirtualHostFull => $"https://{VirtualHost}/";
    public static string TempPath => "Temp/";
    public static string CssPath => "styles.css";
    public static string MessageIdentifier => "EpubReader-";
    public static string ScrollMessageHead => $"{MessageIdentifier}Scrolled:";
    public static string LinkMessageHead => $"{MessageIdentifier}Link:";
    public static string MainPageName => "EpubReaderUnifiedHtml.html";
}
