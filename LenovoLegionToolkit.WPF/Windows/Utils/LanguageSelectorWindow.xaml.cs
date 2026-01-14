using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Humanizer;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class LanguageSelectorWindow
{
    private readonly TaskCompletionSource<CultureInfo?> _taskCompletionSource = new();

    public Task<CultureInfo?> ShouldContinue => _taskCompletionSource.Task;

    public LanguageSelectorWindow(IEnumerable<CultureInfo> languages, CultureInfo defaultLanguage)
    {
        InitializeComponent();

        var languageList = languages.ToList();
        var systemCulture = CultureInfo.CurrentUICulture;

        var selectedLanguage = languageList.FirstOrDefault(l => l.Name.Equals(systemCulture.Name, StringComparison.OrdinalIgnoreCase))
                               ?? languageList.FirstOrDefault(l => l.TwoLetterISOLanguageName == systemCulture.TwoLetterISOLanguageName)
                               ?? defaultLanguage;

        _languageComboBox.SetItems(languageList.OrderBy(ci => ci.Name, StringComparer.InvariantCultureIgnoreCase),
            selectedLanguage,
            cc => cc.NativeName.Transform(cc, To.TitleCase));
    }

    private void LanguageSelectorWindow_OnClosed(object? sender, EventArgs e) => _taskCompletionSource.TrySetResult(null);

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        _languageComboBox.TryGetSelectedItem(out CultureInfo? cultureInfo);
        _taskCompletionSource.TrySetResult(cultureInfo);
        Close();
    }
}
