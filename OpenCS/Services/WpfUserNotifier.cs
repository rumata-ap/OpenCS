using System.Windows;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Уведомления окнами <see cref="MessageBox"/> без владельца; вызывать из UI-потока.</summary>
public sealed class WpfUserNotifier : IUserNotifier
{
    public void Info(string message, string? title = null) =>
        MessageBox.Show(message, title ?? Loc.S("Info"), MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warning(string message, string? title = null) =>
        MessageBox.Show(message, title ?? Loc.S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, string? title = null) =>
        MessageBox.Show(message, title ?? Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string? title = null) =>
        MessageBox.Show(message, title ?? Loc.S("Warning"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
}
