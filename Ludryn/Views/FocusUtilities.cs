using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

namespace Ludryn.Views;

public static class FocusUtilities
{
    public static bool TryGetFocusedElement(FrameworkElement scope, out FrameworkElement focusedElement)
    {
        focusedElement = null!;

        if (scope.XamlRoot is null)
        {
            return false;
        }

        try
        {
            focusedElement = FocusManager.GetFocusedElement(scope.XamlRoot) as FrameworkElement ?? null!;
            return focusedElement is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static T? FindDescendant<T>(DependencyObject root) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild && typedChild.Visibility == Visibility.Visible)
            {
                return typedChild;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            var nested = FindDescendantByName(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static FrameworkElement? GetParent(FrameworkElement element) =>
        VisualTreeHelper.GetParent(element) as FrameworkElement;
}
