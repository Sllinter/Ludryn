using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Ludryn.Views;

public interface IGamepadFocusablePage
{
    void FocusInitialElement();
    bool HandleGamepadMove(FocusNavigationDirection direction);
    bool HandleGamepadBack();
    bool HandleGamepadAccept(FrameworkElement focusedElement);
    bool HandleGamepadX();
    bool HandleGamepadY();
    bool HandleGamepadOptions();
}

public interface IGamepadHintProvider
{
    GamepadHints GetGamepadHints();
}

public sealed record GamepadHints(
    string? Accept = null,
    string? X = null,
    string? Y = null,
    string? Back = null);
