using CommunityToolkit.Mvvm.ComponentModel;
using Ludryn.Models;

namespace Ludryn.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public IReadOnlyList<Game> RecentGames { get; }

    public HomeViewModel(IReadOnlyList<Game> recentGames)
    {
        RecentGames = recentGames;
    }
}
