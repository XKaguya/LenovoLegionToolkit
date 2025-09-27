using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionSpaceDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths => [];
    protected override IEnumerable<string> ServiceNames => ["DAService"];
    protected override IEnumerable<string> ProcessNames => ["LegionSpace", "LSDaemon"];
}
