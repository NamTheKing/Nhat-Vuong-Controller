using CommunityToolkit.Mvvm.ComponentModel;
using NhatVuong.Client.Services;

namespace NhatVuong.Client.ViewModels;

public abstract partial class ViewModelBase(DialogService dialogs) : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? message;

    protected DialogService Dialogs { get; } = dialogs;

    /// <summary>Runs an action, showing failures inline (list refreshes) or as a dialog (user actions).</summary>
    protected async Task RunAsync(Func<Task> action, bool dialogOnError = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Message = null;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
            if (dialogOnError)
            {
                await Dialogs.ErrorAsync(ex);
            }
            else
            {
                Message = ex.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
