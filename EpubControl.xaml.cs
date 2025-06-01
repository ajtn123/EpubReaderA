using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using VersOne.Epub;

namespace EpubReaderA;

public partial class EpubControl
{
    public EpubControl()
    {
        InitializeComponent();
    }

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


        Directory.CreateDirectory(Constants.TempPath);

        foreach (var lt in Book.Content.Html.Local) WriteTemp(lt);
        foreach (var lt in Book.Content.Css.Local) WriteTemp(lt);
        foreach (var lb in Book.Content.Images.Local) WriteTemp(lb);
        foreach (var lb in Book.Content.Fonts.Local) WriteTemp(lb);
        foreach (var lb in Book.Content.Audio.Local) WriteTemp(lb);

        WriteTemp(Book.Content.Cover);
        WriteTemp(Book.Content.NavigationHtmlFile);

        Deobfuscate();

        await WebView.EnsureCoreWebView2Async();

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(Constants.VirtualHost,
            Constants.TempPath, CoreWebView2HostResourceAccessKind.Allow);

        if (File.Exists(Constants.CssPath))
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($$"""
                      (function addGlobalStyle() {
                      function tryInjectStyle() {
                          if (document.head) {
                              const style = document.createElement('style');
                              style.type = 'text/css';
                              style.innerHTML = `{{await File.ReadAllTextAsync(Constants.CssPath)}}`;
                              document.head.prepend(style);
                          } else {
                              setTimeout(tryInjectStyle, 50);
                          }
                      }
                      tryInjectStyle();
                  })();
                  """);

        Navigate(Book.Navigation?.FirstOrDefault()?.Link?.ContentFileUrl ?? Book.ReadingOrder.FirstOrDefault()?.Key);


        var menuItems = Book.Navigation?.Select(ro => new MenuItemModel(ro));
        var coverItem = Book.Content.Cover != null ? new MenuItemModel("Cover", Book.Content.Cover.Key) : null;
        var navItem = Book.Content.NavigationHtmlFile != null
            ? new MenuItemModel("Navigation", Book.Content.NavigationHtmlFile.Key)
            : null;

        Menu.ItemsSource = (menuItems ?? []).Concat(new[] { coverItem, navItem }.Where(x => x != null));


        Slider.Maximum = Book.ReadingOrder.Count;
        Slider.Minimum = 1;
        Slider.ValueChanged += (_, args) =>
        {
            var target = Book.ReadingOrder[(int)args.NewValue - 1].Key;
            if (CurrentPage != target)
                Navigate(target);
        };

        Prev.Click += (_, _) =>
        {
            var targetIndex = Math.Clamp(CurrentIndex - 1, 0, Book.ReadingOrder.Count - 1);
            var target = Book.ReadingOrder[targetIndex].Key;
            Navigate(target);
        };
        Next.Click += (_, _) =>
        {
            var targetIndex = Math.Clamp(CurrentIndex + 1, 0, Book.ReadingOrder.Count - 1);
            var target = Book.ReadingOrder[targetIndex].Key;
            Navigate(target);
        };

        Expand.Click += (_, _) =>
            Expander.Visibility = Expander.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        WebView.CoreWebView2.SourceChanged += (_, _) =>
        {
            Console.WriteLine($"Changed To: {CurrentPage}");
            var index = Book.ReadingOrder.FindIndex(a => a.Key == CurrentPage.Split('#').First()) + 1;
            if (index == 0) return;
            Slider.Value = index;
            Console.WriteLine($"To Index: {index}/{Book.ReadingOrder.Count}");
        };

        // File.WriteAllText("book.json", JsonHelper.Serialize(Book, true));
    }

    public string CurrentPage => WebView.CoreWebView2.Source.Replace(Constants.VirtualHostFull, "");
    public int CurrentIndex => Book?.ReadingOrder.FindIndex(a => a.Key == CurrentPage) ?? -1;

    public void Navigate(string? key, string? a = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        Console.WriteLine($"To Path: {key} | {(a != null ? "#" : "")}{a}");
        Console.WriteLine(File.Exists($"{Constants.TempPath}{key}"));
        if (!File.Exists($"{Constants.TempPath}{key}"))
        {
            key = key.Split('.').Last() + "/" + key;
        }

        WebView.CoreWebView2.Navigate($"{Constants.VirtualHostFull}{key}{(a != null ? "#" : "")}{a}");
    }

    private void WriteTemp(EpubLocalTextContentFile? file)
    {
        if (file is null) return;
        var path = $"{Constants.TempPath}{file.Key}";
        var dir = Path.GetDirectoryName(path) ?? "";
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, file.Content);
    }

    private void WriteTemp(EpubLocalByteContentFile? file)
    {
        if (file is null) return;
        var path = $"{Constants.TempPath}{file.Key}";
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

            using var sha1 = SHA1.Create();
            var key = sha1.ComputeHash(Encoding.UTF8.GetBytes(id));

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
                var fontPath = Constants.TempPath + Path.GetRelativePath(rootPath, fontUri);
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
                            int b = input.ReadByte();
                            if (b == -1) break;

                            byte deobfuscated = (byte)(b ^ key[i]);
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