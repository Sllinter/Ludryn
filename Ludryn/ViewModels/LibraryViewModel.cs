using CommunityToolkit.Mvvm.ComponentModel;
using Ludryn.Models;

namespace Ludryn.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    public IReadOnlyList<Game> Games { get; }

    public LibraryViewModel(IReadOnlyList<Game> games)
    {
        Games = games;
    }
}
