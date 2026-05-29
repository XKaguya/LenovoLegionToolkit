using System;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using ThemeType = Wpf.Ui.Appearance.ThemeType;
using BackgroundType = Wpf.Ui.Appearance.BackgroundType;
using Accent = Wpf.Ui.Appearance.Accent;

namespace LenovoLegionToolkit.WPF.Utils;

public class ThemeManager
{
    private static readonly RGBColor DefaultAccentColor = new(255, 33, 33);

    private readonly ApplicationSettings _settings;
    private readonly SystemThemeListener _listener;

    public event EventHandler? ThemeApplied;

    public ThemeManager(SystemThemeListener systemThemeListener, ApplicationSettings settings)
    {
        _listener = systemThemeListener;
        _settings = settings;

        _listener.Changed += (_, _) => Application.Current.Dispatcher.Invoke(Apply);
    }

    public void Apply()
    {
        SetTheme();
        SetColor();

        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    public RGBColor GetAccentColor()
    {
        switch (_settings.Store.AccentColorSource)
        {
            case AccentColorSource.Custom:
                return _settings.Store.AccentColor ?? DefaultAccentColor;
            case AccentColorSource.System:
                try
                {
                    return SystemTheme.GetAccentColor();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't check system accent color; using default.", ex);

                    return DefaultAccentColor;
                }
            default:
                return DefaultAccentColor;
        }
    }

    private bool IsDarkMode()
    {
        var theme = _settings.Store.Theme;

        switch (theme)
        {
            case Theme.Dark:
                return true;
            case Theme.Light:
                return false;
            case Theme.System:
                try
                {
                    return SystemTheme.IsDarkMode();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't check system theme; assuming Dark Mode.", ex);

                    return true;
                }
            default:
                return true;
        }
    }

    private void SetTheme()
    {
        var theme = IsDarkMode() ? ThemeType.Dark : ThemeType.Light;
        Wpf.Ui.Appearance.Theme.Apply(theme, GetBackgroundType(_settings.Store.BackdropType), false);
    }

    public static BackgroundType GetBackgroundType(WindowBackdropType backdropType)
    {
        return backdropType switch
        {
            WindowBackdropType.Mica => BackgroundType.Mica,
            WindowBackdropType.Acrylic => BackgroundType.Acrylic,
            WindowBackdropType.Tabbed => BackgroundType.Tabbed,
            _ => BackgroundType.None
        };
    }

    private void SetColor()
    {
        var accentColor = GetAccentColor().ToColor();
        Accent.Apply(systemAccent: accentColor,
            primaryAccent: accentColor,
            secondaryAccent: accentColor,
            tertiaryAccent: accentColor);
    }
}
