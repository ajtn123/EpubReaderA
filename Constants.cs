namespace EpubReaderA;

public static class Constants
{
    public static string VirtualHost => "epub.temp";
    public static string VirtualHostFull => $"https://{VirtualHost}/";
    public static string TempPath => "Temp/";
    public static string CssPath => "styles.css";
    public static string ScrollMessageHead => "scrolled:";
    public static string ExternalLinkMessageHead => "externalLink:";
    public static string MainPageName => "EpubReaderUnifiedHtml.html";
}
