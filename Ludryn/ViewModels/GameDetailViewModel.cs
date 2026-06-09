using CommunityToolkit.Mvvm.ComponentModel;
using Ludryn.Models;

namespace Ludryn.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    public Game Game { get; }

    public GameDetailViewModel(Game game)
    {
        Game = game;
    }
}
