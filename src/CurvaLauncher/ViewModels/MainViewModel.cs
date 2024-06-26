﻿using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CurvaLauncher.Messages;
using CurvaLauncher.Models;
using CurvaLauncher.Services;
using CurvaLauncher.Utilities;

namespace CurvaLauncher.ViewModels;

public partial class MainViewModel : ObservableObject, IRecipient<SaveQueryMessage>
{
    private readonly PluginService _pluginService;
    private readonly ConfigService _configService;

    public MainViewModel(
        PluginService pluginService,
        ConfigService configService,
        IMessenger messenger)
    {
        _pluginService = pluginService;
        _configService = configService;

        messenger.Register(this);
    }

    [ObservableProperty]
    string? _lastInvokedQueryText;

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QueryResultModel> queryResults = new();

    [ObservableProperty]
    private QueryResultModel? selectedQueryResult;

    [ObservableProperty]
    private int selectedQueryResultIndex = 0;

    public bool HasQueryResult => QueryResults.Count > 0;

    [RelayCommand]
    public async Task QueryCore(CancellationToken cancellationToken)
    {
        SelectedQueryResult = null;

        var dispatcher = Dispatcher.CurrentDispatcher;
        var queryText = QueryText;

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            SortedCollection<QueryResultModel, float> queryResults = new()
            {
                SortingRoot = m => m.Weight,
                Descending = true,
            };

            await Task.Run(async () =>
            {
                foreach (var pluginInstance in _pluginService.PluginInstances)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    if (!pluginInstance.IsEnabled)
                        continue;

                    await pluginInstance.InitTask;

                    try
                    {
                        await foreach (var result in pluginInstance.QueryAsync(queryText))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                return;

                            var model = QueryResultModel.Create(pluginInstance, result);
                            queryResults.Add(model);

                            dispatcher.Invoke(() =>
                            {
                                model.SetFallbackIcon(() => pluginInstance.Plugin.Icon);

                                for (int i = 0; i < queryResults.Count; i++)
                                {
                                    if (QueryResults.Count > i)
                                        QueryResults[i] = queryResults[i];
                                    else
                                        QueryResults.Add(queryResults[i]);
                                }

                                if (SelectedQueryResult == null)
                                    SelectedQueryResultIndex = 0;
                            });

                            if (cancellationToken.IsCancellationRequested)
                                return;
                        }
                    }
                    catch
                    {

                    }
                }
            });

            if (cancellationToken.IsCancellationRequested)
                return;

            while (QueryResults.Count > queryResults.Count)
                QueryResults.RemoveAt(QueryResults.Count - 1);
        }
        else
        {
            QueryResults.Clear();
        }

        OnPropertyChanged(nameof(HasQueryResult));
    }

    [RelayCommand]
    public void Query()
    {
        if (QueryCoreCommand.IsRunning)
            QueryCoreCommand.Cancel();

        QueryCoreCommand.Execute(null);
    }

    [RelayCommand]
    public async Task InvokeSelected()
    {
        if (SelectedQueryResult == null)
            return;

        await SelectedQueryResult.InvokeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    public void SelectNext()
    {
        if (QueryResults.Count == 0)
            return;

        SelectedQueryResultIndex = (SelectedQueryResultIndex + 1) % QueryResults.Count;
    }

    [RelayCommand]
    public void SelectPrev()
    {
        if (QueryResults.Count == 0)
        {
            if (LastInvokedQueryText != null && string.IsNullOrWhiteSpace(QueryText))
                QueryText = LastInvokedQueryText;

            return;
        }

        int newIndex = (SelectedQueryResultIndex - 1) % QueryResults.Count;
        if (newIndex == -1)
            newIndex = QueryResults.Count - 1;

        SelectedQueryResultIndex = newIndex;
    }

    void IRecipient<SaveQueryMessage>.Receive(SaveQueryMessage message)
    {
        LastInvokedQueryText = QueryText;
    }
}
