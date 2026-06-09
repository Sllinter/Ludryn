using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;

namespace Ludryn.Views;

public static class GameCardSelection
{
    private sealed class SelectionState
    {
        public bool? Selected { get; set; }
        public Storyboard? Storyboard { get; set; }
    }

    private static readonly ConditionalWeakTable<FrameworkElement, SelectionState> SelectionStates = new();

    public static void Apply(FrameworkElement card, bool selected)
    {
        var target = GetTransformTarget(card);
        if (target is not null)
        {
            var state = SelectionStates.GetOrCreateValue(target);
            if (state.Selected == selected)
            {
                return;
            }

            state.Selected = selected;
            state.Storyboard?.Stop();
            Canvas.SetZIndex(target, selected ? 10 : 0);
            target.RenderTransformOrigin = new Point(0.5, 0.5);

            var storyboard = new Storyboard();
            if (target.RenderTransform is CompositeTransform composite)
            {
                AddAnimation(storyboard, composite, nameof(CompositeTransform.ScaleX), selected ? 1.1 : 1);
                AddAnimation(storyboard, composite, nameof(CompositeTransform.ScaleY), selected ? 1.1 : 1);
                AddAnimation(storyboard, composite, nameof(CompositeTransform.TranslateY), selected ? -12 : 0);
            }
            else if (target.RenderTransform is ScaleTransform scale)
            {
                AddAnimation(storyboard, scale, nameof(ScaleTransform.ScaleX), selected ? 1.1 : 1);
                AddAnimation(storyboard, scale, nameof(ScaleTransform.ScaleY), selected ? 1.1 : 1);
            }

            if (storyboard.Children.Count > 0)
            {
                state.Storyboard = storyboard;
                storyboard.Begin();
            }
        }

        var border = FocusUtilities.FindDescendant<Border>(target ?? card);
        if (border is not null)
        {
            border.BorderThickness = selected ? new Thickness(2.5) : new Thickness(1);
            border.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(42, 255, 255, 255));
        }
    }

    private static FrameworkElement? GetTransformTarget(FrameworkElement card)
    {
        if (card.RenderTransform is CompositeTransform or ScaleTransform)
        {
            return card;
        }

        var nestedGrid = FocusUtilities.FindDescendant<Grid>(card);
        return nestedGrid?.RenderTransform is CompositeTransform or ScaleTransform ? nestedGrid : nestedGrid;
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(190),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
