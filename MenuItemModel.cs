using System.Windows;
using System.Windows.Input;
using VersOne.Epub;

namespace EpubReaderA;

public class MenuItemModel
{
    public MenuItemModel(EpubNavigationItem navigationItem)
    {
        NavigationItem = navigationItem;
        Nested = navigationItem.NestedItems.Select(ro => new MenuItemModel(ro));
        NavigateCommand = new NavigateCommand
        { Key = navigationItem.Link?.ContentFileUrl, Anchor = NavigationItem.Link?.Anchor };
        Title = navigationItem.Title;
    }

    public MenuItemModel(string title, string? key, string? anchor = null)
    {
        NavigateCommand = new NavigateCommand() { Key = key, Anchor = anchor };
        Title = title;
    }

    public EpubNavigationItem? NavigationItem { get; }
    public string Title { get; }
    public IEnumerable<MenuItemModel>? Nested { get; }
    public ICommand NavigateCommand { get; }
}

public class NavigateCommand : ICommand
{
    public required string? Key { get; init; }
    public required string? Anchor { get; init; }

    public void Execute(object? parameter) =>
        // ReSharper disable once AccessToStaticMemberViaDerivedType
        (Window.GetWindow(App.Current.MainWindow!) as MainWindow)?.CurrentEpubView.NavigateAsync(Key, Anchor);

    public bool CanExecute(object? parameter) => Key != null;

    public event EventHandler? CanExecuteChanged;
}