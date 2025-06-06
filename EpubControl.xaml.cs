using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using VersOne.Epub;

namespace EpubReaderA;

public partial class EpubControl
{
    public EpubControl()
        => InitializeComponent();

    public EpubControl(string path)
    {
        InitializeComponent();
        _ = Init(path);
    }

    public EpubBook? Book { get; set; }

    private async Task Init(string path)
    {
        Console.WriteLine(path);
        try
        {
            Book = await EpubReader.ReadBookAsync(path);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return;
        }

        BookTempPath = $"{Constants.TempPath}{Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(Book.Title)))[..6]}/";

        foreach (var lt in Book.Content.Html.Local) WriteTemp(lt);
        foreach (var lt in Book.Content.Css.Local) WriteTemp(lt);
        foreach (var lb in Book.Content.Images.Local) WriteTemp(lb);
        foreach (var lb in Book.Content.Fonts.Local) WriteTemp(lb);
        foreach (var lb in Book.Content.Audio.Local) WriteTemp(lb);

        WriteTemp(Book.Content.Cover);
        WriteTemp(Book.Content.NavigationHtmlFile);

        Deobfuscate();

        await WebView.EnsureCoreWebView2Async();

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(Constants.VirtualHost, BookTempPath, CoreWebView2HostResourceAccessKind.Allow);

        if (File.Exists(Constants.CssPath))
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($$"""
                (function addGlobalStyle() {
                  function injectStyle() {
                    if (document.head) {
                      const style = document.createElement('style');
                      style.type = 'text/css';
                      style.innerHTML = `{{await File.ReadAllTextAsync(Constants.CssPath)}}`;
                      document.head.prepend(style);
                    } else setTimeout(tryInjectStyle, 50);
                  }
                  injectStyle();
                })();
                """);

        var unifiedHtml = $$"""
            <!DOCTYPE html>
                <html>
                    <head>
                        <meta charset="UTF-8" />
                        <script>
                            function resizeIframe(iframe) {
                              if (!iframe) return;

                              function updateSize() {
                                const html = iframe.contentDocument.documentElement;
                                const height = html.getBoundingClientRect().height + 35;
                                iframe.style.height = height + 'px';
                              }

                              updateSize();
                              new ResizeObserver(updateSize).observe(iframe.contentDocument.body);
                            }

                            function scrollToAnchor(iframeId, anchorId) {
                              if (!iframeId) return;
                              const iframe = document.getElementById(iframeId);
                              if (!iframe) return;
                              if (anchorId) {
                                const doc = iframe.contentDocument;
                                const target = doc.getElementById(anchorId) || doc.querySelector(`[name='${anchorId}']`);
                                if (target) target.scrollIntoView({ behavior: 'smooth' });
                              } else iframe.scrollIntoView({ behavior: 'smooth' });
                            }

                            function injectLinkScript(iframe) {
                                const doc = iframe.contentDocument;
                                const script = doc.createElement('script');
                                script.textContent = `
                                  (function () {
                                    document.querySelectorAll('a[href]').forEach((link) => {
                                      link.addEventListener('click', function (e) {
                                        e.preventDefault();
                                        window.parent.chrome.webview.postMessage("{{Constants.LinkMessageHead}}" + this.href);
                                      });
                                    });
                                  })();
                                `;
                                doc.head.appendChild(script);
                            }
                            
                            window.addEventListener('scroll', () => {
                              for(const iframe of document.querySelectorAll('iframe')){
                                const rect = iframe.getBoundingClientRect();
                                const visibleRatio = rect.height > 0 ? (Math.min(window.innerHeight, rect.bottom) - Math.max(0, rect.top)) / window.innerHeight : 0;
                                if (visibleRatio > 0.6) {
                                  window.chrome.webview.postMessage("{{Constants.ScrollMessageHead}}" + iframe.id);
                                  break;
                                }
                              }
                            });
                        </script>
                        <style>
                            body { margin: 0; }
                            iframe {
                                display: block;
                                border: none;
                                width: 100%;
                            }                        
                        </style>
                    </head>
                <body>
                    {{Book.ReadingOrder.Aggregate("", (a, b) => a + $"<iframe src=\"{Constants.VirtualHostFull}{b.Key}\" id=\"{b.Key}\" onload=\"resizeIframe(this);injectLinkScript(this);\"></iframe>")}}
                </body>
            </html>
            """;

        Directory.CreateDirectory(BookTempPath);
        File.WriteAllText($"{BookTempPath}{Constants.MainPageName}", unifiedHtml);
        WebView.CoreWebView2.Navigate($"{Constants.VirtualHostFull}{Constants.MainPageName}");

        CurrentSec = Book.ReadingOrder[0].Key;


        var menuItems = Book.Navigation?.Select(ro => new MenuItemModel(ro));
        var coverItem = Book.Content.Cover != null ? new MenuItemModel("Cover", Book.Content.Cover.Key) : null;
        var navItem = Book.Content.NavigationHtmlFile != null ? new MenuItemModel("Navigation", Book.Content.NavigationHtmlFile.Key) : null;

        Menu.ItemsSource = (menuItems ?? []).Concat(new[] { coverItem, navItem }.Where(x => x != null));


        Slider.Maximum = Book.ReadingOrder.Count - 1;
        Slider.Minimum = 0;

        Slider.ValueChanged += (_, args) =>
        {
            var target = Book.ReadingOrder[(int)args.NewValue].Key;
            if (CurrentSec != target)
                _ = NavigateAsync(target);
        };

        Prev.Click += (_, _) =>
        {
            var targetIndex = Math.Clamp(CurrentIndex - 1, 0, Book.ReadingOrder.Count - 1);
            var target = Book.ReadingOrder[targetIndex].Key;
            _ = NavigateAsync(target);
        };
        Next.Click += (_, _) =>
        {
            var targetIndex = Math.Clamp(CurrentIndex + 1, 0, Book.ReadingOrder.Count - 1);
            var target = Book.ReadingOrder[targetIndex].Key;
            _ = NavigateAsync(target);
        };

        Expand.Click += (_, _) =>
            Expander.Visibility = Expander.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        WebView.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var message = e.TryGetWebMessageAsString();
            if (message.StartsWith(Constants.ScrollMessageHead))
            {
                string iframeId = message[Constants.ScrollMessageHead.Length..];
                if (CurrentSec == iframeId) return;
                Debug.WriteLine($"Scrolled to iframe: {iframeId}");
                CurrentSec = iframeId;
                var index = Book.ReadingOrder.FindIndex(a => a.Key == iframeId);
                if (index == -1) return;
                Slider.Value = index;
            }
            else if (message.StartsWith(Constants.LinkMessageHead))
            {
                var link = message[Constants.LinkMessageHead.Length..];
                if (!link.StartsWith(Constants.VirtualHostFull)) Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
                else
                {
                    var relPath = link.Replace(Constants.VirtualHostFull, "");
                    string key = ""; string anchor = "";
                    if (relPath.Contains('#'))
                    {
                        var ka = relPath.Split('#');
                        key = ka[0]; anchor = ka[1];
                    }
                    else key = relPath;
                    _ = NavigateAsync(key, anchor);
                }
            }
            else Trace.WriteLine($"UnknownMessage: {message}");
        };

        // File.WriteAllText("book.json", JsonHelper.Serialize(Book, true));
    }

    public string CurrentPage => WebView.CoreWebView2.Source.Replace(Constants.VirtualHostFull, "");
    public string CurrentSec { get; set; } = "";
    public int CurrentIndex => Book?.ReadingOrder.FindIndex(a => a.Key == CurrentSec) ?? -1;
    public string BookTempPath { get; set; } = $"{Constants.TempPath}{DateTime.Now.Ticks.ToString()[^4..]}/";

    public async Task NavigateAsync(string? key, string? a = null)
    {
        if (Book == null || string.IsNullOrWhiteSpace(key)) return;

        Trace.WriteLine($"To Path: {key} | {(a != null ? "#" : "")}{a}");
        if (!File.Exists($"{BookTempPath}{key}"))
            key = key.Split('.').Last() + "/" + key;

        if (!Book.ReadingOrder.Any(a => a.Key == key))
        {
            WebView.CoreWebView2.Navigate($"{Constants.VirtualHostFull}{key}{(string.IsNullOrWhiteSpace(a) ? "" : "#")}{a}");
            return;
        }

        if (CurrentPage != Constants.MainPageName)
        {
            WebView.CoreWebView2.Navigate($"{Constants.VirtualHostFull}{Constants.MainPageName}");
            await WaitForPageLoadAsync();
        }

        if (!string.IsNullOrWhiteSpace(a))
            await WebView.ExecuteScriptAsync($"scrollToAnchor('{key}','{a}');");
        else
            await WebView.ExecuteScriptAsync($"scrollToAnchor('{key}');");
    }

    public async Task WaitForPageLoadAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            WebView.CoreWebView2.NavigationCompleted -= Handler;

            if (e.IsSuccess)
                tcs.SetResult(true);
            else
                tcs.SetException(new Exception($"Navigation failed: {e.WebErrorStatus}"));
        }

        WebView.CoreWebView2.NavigationCompleted += Handler;

        await tcs.Task;
    }


    private void WriteTemp(EpubLocalTextContentFile? file)
    {
        if (file is null) return;
        var path = $"{BookTempPath}{file.Key}";
        var dir = Path.GetDirectoryName(path) ?? "";
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, file.Content);
    }

    private void WriteTemp(EpubLocalByteContentFile? file)
    {
        if (file is null) return;
        var path = $"{BookTempPath}{file.Key}";
        var dir = Path.GetDirectoryName(path) ?? "";
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, file.Content);
    }

    private void Deobfuscate()
    {
        var path = Book?.FilePath;
        if (path == null) return;

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("META-INF/encryption.xml");

        if (entry != null)
        {
            var id = Book?.Schema.Package.Metadata.Identifiers
                .FirstOrDefault(a => a?.Id == Book.Schema.Package.UniqueIdentifier, null)?
                .Identifier.Replace(" ", "");
            if (id == null) return;
            var key = SHA1.HashData(Encoding.UTF8.GetBytes(id));

            using var reader = new StreamReader(entry.Open());

            var xml = XDocument.Load(reader);
            XNamespace enc = "http://www.w3.org/2001/04/xmlenc#";

            var fontUris = xml.Descendants(enc + "EncryptedData")
                .Where(ed =>
                    (string?)ed.Element(enc + "EncryptionMethod")?.Attribute("Algorithm") ==
                    "http://www.idpf.org/2008/embedding")
                .Select(ed =>
                    (string?)ed.Element(enc + "CipherData")?.Element(enc + "CipherReference")?.Attribute("URI"))
                .Where(uri => !string.IsNullOrEmpty(uri))
                .ToList();

            var rootPath = Book?.Schema.ContentDirectoryPath;
            if (rootPath == null) return;

            foreach (var fontUri in fontUris)
            {
                Console.WriteLine($"Deobfuscating: {fontUri}");
                if (string.IsNullOrWhiteSpace(fontUri)) return;
                var fontPath = BookTempPath + Path.GetRelativePath(rootPath, fontUri);
                Console.WriteLine($"Temp Path: {fontPath}");

                var tempPath = fontPath + ".tmp";

                using (var input = new FileStream(fontPath, FileMode.Open, FileAccess.Read))
                using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    const int maxBytes = 1040;
                    var keyLen = key.Length;
                    var processed = 0;

                    while (processed < maxBytes && input.Position < input.Length)
                    {
                        for (var i = 0; i < keyLen && processed < maxBytes && input.Position < input.Length; i++)
                        {
                            var b = input.ReadByte();
                            if (b == -1) break;

                            var deobfuscated = (byte)(b ^ key[i]);
                            output.WriteByte(deobfuscated);
                            processed++;
                        }
                    }

                    input.CopyTo(output);
                }

                File.Delete(fontPath);
                File.Move(tempPath, fontPath);
            }
        }
        else
            Console.WriteLine("encryption.xml not found.");
    }
}
