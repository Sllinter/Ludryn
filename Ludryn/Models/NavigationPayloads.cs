using Ludryn.Services;

namespace Ludryn.Models;

public sealed record LibraryNavigation(MockDataService DataService, string? Platform);
public sealed record GameDetailNavigation(Game Game, MockDataService DataService);
