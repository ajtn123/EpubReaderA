using System.IO;
using System.Windows;

namespace EpubReaderA;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();

        FileInfo? epubFile = null;
        var args = Environment.GetCommandLineArgs();
        if (args.FirstOrDefault(x => { var f = new FileInfo(x!); return f.Exists && f.Extension.Equals(".epub", StringComparison.OrdinalIgnoreCase); }, null) is string epubFilePath)
            epubFile = new FileInfo(epubFilePath);

        if (Environment.ProcessPath is string processPath)
            if (new FileInfo(processPath).DirectoryName is string dir)
                Environment.CurrentDirectory = dir;
            else Close();

        if (epubFile != null) ShowEpub(epubFile.FullName);

        CurrentEpubView ??= new EpubControl();

        Drop += (_, args) =>
        {
            if (args.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)args.Data.GetData(DataFormats.FileDrop);
                if (files == null) return;

                ShowEpub(files[0]);
                Console.WriteLine(files[0]);
            }
            else if (args.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = (string?)args.Data.GetData(DataFormats.Text);
                if (string.IsNullOrWhiteSpace(text)) return;

                ShowEpub(text);
                Console.WriteLine(text);
            }
            else
            {
                Console.WriteLine(args.Data.GetFormats().Aggregate("", (a, b) => a + "||" + b));
            }
        };
    }

    public void ShowEpub(string path)
    {
        var view = new EpubControl(path);
        CurrentEpubView = view;
        Content = view;
    }

    private EpubControl? currentEpubView;
    public EpubControl? CurrentEpubView { get => currentEpubView; private set { currentEpubView?.WebView.Dispose(); currentEpubView = value; } }
}