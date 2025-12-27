using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using ZenStates.Core;

namespace LenovoLegionToolkit.WPF.Windows.Overclocking.Amd;

public partial class AmdOverclocking : UiWindow
{
    public readonly AmdOverclockingController Controller = IoCContainer.Resolve<AmdOverclockingController>();

    private NumberBox[] _coreBoxes;
    private bool _isInitialized;

    public class OverclockingProfile
    {
        public double? FMax { get; set; }
        public List<double?> CoreValues { get; set; } = new();
    }

    public AmdOverclocking()
    {
        InitializeComponent();
        _initCoreArray();

        IsVisibleChanged += async (s, e) =>
        {
            if ((bool)e.NewValue && _isInitialized) await RefreshAsync();
        };

        Loaded += async (s, e) => await InitAndRefreshAsync();
    }

    private void _initCoreArray()
    {
        _coreBoxes = [ _core0,  _core1,  _core2,  _core3, _core4,  _core5,  _core6,  _core7,
            _core8,  _core9,  _core10, _core11, _core12, _core13, _core14, _core15 ];
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        _statusInfoBar.Title = title;
        _statusInfoBar.Message = message;
        _statusInfoBar.Severity = severity;
        _statusInfoBar.IsOpen = true;

        Task.Delay(5000).ContinueWith(_ => Dispatcher.Invoke(() => _statusInfoBar.IsOpen = false));
    }

    private async Task InitAndRefreshAsync()
    {
        if (!_isInitialized)
        {
            try
            {
                await Controller.InitializeAsync();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Initialization Failed: {ex.Message}");
                return;
            }
        }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var cpu = Controller.GetCpu();
            _fMaxNumberBox.Value = (double)Controller.GetFMaxFrequency();

            if (cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin == 0) return;

            uint activeCoresCount = cpu.info.topology.physicalCores;

            for (var i = 0; i < _coreBoxes.Length; i++)
            {
                var control = _coreBoxes[i];
                if (i >= activeCoresCount)
                {
                    control.IsEnabled = false;
                    control.Visibility = Visibility.Collapsed;
                    continue;
                }

                bool isCoreActive = IsCoreActive(cpu, i);
                control.IsEnabled = isCoreActive;

                if (isCoreActive)
                {
                    uint? margin = cpu.GetPsmMarginSingleCore(EncodeCoreMarginBitmask(cpu, i));
                    if (margin.HasValue)
                    {
                        control.Value = (double)(int)margin.Value;
                    }
                }
                else
                {
                    control.Value = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Refresh Failed: {ex.Message}");
        }
    }

    private bool IsCoreActive(Cpu cpu, int coreIndex)
    {
        int mapIndex = coreIndex < 8 ? 0 : 1;
        return ((~cpu.info.topology.coreDisableMap[mapIndex] >> (coreIndex % 8)) & 1) == 1;
    }

    private uint EncodeCoreMarginBitmask(Cpu cpu, int coreIndex, int coresPerCCD = 8)
    {
        if (cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
        {
            return (uint)coreIndex;
        }

        int ccdIndex = coreIndex / coresPerCCD;
        int localCoreIndex = coreIndex % coresPerCCD;
        int mask = (ccdIndex << 8) | localCoreIndex;
        return (uint)(mask << 20);
    }

    #region Event Handlers
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var cpu = Controller.GetCpu();

            if (_fMaxNumberBox.Value.HasValue)
            {
                uint fmaxVal = (uint)_fMaxNumberBox.Value.Value;
                bool res = cpu.SetFMax(fmaxVal);
                Log.Instance.Trace($"FMax Set {(res ? "Success" : "Failed")}: {fmaxVal}");
            }

            if (cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0)
            {
                for (var i = 0; i < _coreBoxes.Length; i++)
                {
                    var control = _coreBoxes[i];
                    if (!control.IsEnabled || !control.Value.HasValue) continue;

                    int marginValue = Convert.ToInt32(control.Value.Value);
                    bool res = cpu.SetPsmMarginSingleCore(EncodeCoreMarginBitmask(cpu, i), marginValue);

                    if (!res) {Log.Instance.Trace($"Core {i} apply failed with value {marginValue}");}
                }
            }

            ShowStatus("Success", "Overclocking settings applied to hardware.", InfoBarSeverity.Success);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Apply Failed: {ex.Message}");
            ShowStatus("Apply Error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var control in _coreBoxes.Where(c => c.IsEnabled))
        {
            control.Value = 0;
        }
        Log.Instance.Trace($"UI Values reset to 0 (Not yet applied).");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = new OverclockingProfile
            {
                FMax = _fMaxNumberBox.Value,
                CoreValues = _coreBoxes.Select(b => b.Value).ToList()
            };

            var sfd = new SaveFileDialog
            {
                Filter = "JSON Profile (*.json)|*.json",
                FileName = "AmdOverclockingProfile.json"
            };

            if (sfd.ShowDialog() == true)
            {
                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                Log.Instance.Trace($"Profile saved to {sfd.FileName}");
                ShowStatus("Profile Exported", $"Settings saved to {Path.GetFileName(sfd.FileName)}", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save Failed: {ex.Message}");
            ShowStatus("Export Failed", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Profile (*.json)|*.json",
                Title = "Load Overclocking Profile"
            };

            if (ofd.ShowDialog() != true) return;

            string json = File.ReadAllText(ofd.FileName);
            var profile = JsonSerializer.Deserialize<OverclockingProfile>(json);

            if (profile == null)
            {
                Log.Instance.Trace($"Load Failed: Profile is null.");
                ShowStatus("Invalid File", "The selected JSON is empty or invalid.", InfoBarSeverity.Error);
                return;
            }

            if (profile.FMax.HasValue)
            {
                _fMaxNumberBox.Value = profile.FMax.Value;
            }

            if (profile is { CoreValues: not null })
            {
                for (int i = 0; i < _coreBoxes.Length && i < profile.CoreValues.Count; i++)
                {
                    var control = _coreBoxes[i];

                    if (control.IsEnabled)
                    {
                        var loadedValue = profile.CoreValues[i];

                        control.Value = loadedValue ?? 0;
                    }
                }
            }

            Log.Instance.Trace($"Profile loaded successfully from {ofd.FileName}");
            ShowStatus("Profile Imported", "Settings loaded into UI. Click 'Apply' to save to hardware.", InfoBarSeverity.Informational);
        }
        catch (JsonException ex)
        {
            Log.Instance.Trace($"Load Failed (Invalid JSON): {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Load Failed: {ex.Message}");
            ShowStatus("Import Failed", "Could not read the profile file.", InfoBarSeverity.Error);
        }
    }

    #endregion
}
