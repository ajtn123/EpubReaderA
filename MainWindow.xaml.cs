using System.Windows;

namespace EpubReaderA;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();

        ShowEpub("C:/Users/chenf/Downloads/epub30-spec.epub");

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

    public EpubControl CurrentEpubView { get; private set; }
}