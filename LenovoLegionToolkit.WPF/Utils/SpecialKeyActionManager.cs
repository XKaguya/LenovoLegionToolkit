using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public class SpecialKeyActionManager
{
    private readonly SpecialKeySettings _settings;
    private readonly AutomationProcessor _automationProcessor;
    private Action? _bringToForeground;

    public SpecialKeyActionManager(SpecialKeySettings settings, AutomationProcessor automationProcessor)
    {
        _settings = settings;
        _automationProcessor = automationProcessor;
    }

    public void WireUp(SpecialKeyListener listener, Action? bringToForeground = null)
    {
        _bringToForeground = bringToForeground;
        listener.CustomKeyHandler = ExecuteAsync;
    }

    private async Task<bool> ExecuteAsync(SpecialKey key)
    {
        var keyInt = (int)key;

        if (!_settings.Store.KeyModes.TryGetValue(keyInt, out var mode) || mode != CustomSpecialKey.Custom)
            return false;

        Log.Instance.Trace($"Custom action triggered for {key}");

        var actions = _settings.Store.KeyActions.GetValueOrDefault(keyInt);

        if (actions is null || actions.Count == 0)
        {
            Log.Instance.Trace($"Bringing to foreground for {key}");
            _bringToForeground?.Invoke();
            return true;
        }

        var currentId = actions[0];

        try
        {
            var pipelines = await _automationProcessor.GetPipelinesAsync().ConfigureAwait(false);
            var pipeline = pipelines.FirstOrDefault(p => p.Id == currentId);
            if (pipeline is not null)
            {
                Log.Instance.Trace($"Running pipeline {currentId} for {key}");
                await _automationProcessor.RunNowAsync(pipeline.Id).ConfigureAwait(false);
                MessagingCenter.Publish(new NotificationMessage(NotificationType.SmartKeySinglePress, pipeline.Name ?? string.Empty));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Running pipeline {currentId} for {key} failed.", ex);
        }

        actions.RemoveAt(0);
        actions.Add(currentId);
        _settings.SynchronizeStore();

        return true;
    }
}
