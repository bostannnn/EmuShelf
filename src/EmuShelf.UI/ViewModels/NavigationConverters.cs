using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EmuShelf.App.ViewModels;

/// <summary>View-only converters for the Desktop console navigation list.</summary>
public static class NavigationConverters
{
    /// <summary>
    /// Decides whether a system's manufacturer header should show. Inputs, in order:
    /// <c>[systemId, groupLeaderIds, isNavigationCollapsed]</c>. Returns true only when the system
    /// leads its manufacturer group (its id is in <c>groupLeaderIds</c>) and the sidebar is
    /// expanded — a collapsed icon rail has no room for a text header.
    /// </summary>
    public static readonly IMultiValueConverter GroupHeaderVisible = new GroupHeaderVisibilityConverter();

    private sealed class GroupHeaderVisibilityConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 3 &&
                values[0] is string id &&
                values[1] is IReadOnlySet<string> groupLeaderIds &&
                values[2] is bool isCollapsed)
            {
                return !isCollapsed && groupLeaderIds.Contains(id);
            }

            return false;
        }
    }
}
