using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.OverDrive;

public class OverDriveCapabilityFeature : AbstractCapabilityFeature<OverDriveState>
{
    public OverDriveCapabilityFeature() : base(CapabilityID.OverDrive)
    {
    }

    protected override async Task<bool> ValidateExtraSupportAsync(MachineInformation mi)
    {
        return await Task.FromResult(Compatibility.GetIsOverdriverSupported()).ConfigureAwait(false);
    }
}
