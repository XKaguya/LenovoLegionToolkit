using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct DriverKeyPressedMessage(DriverKey driverKey) : IMessage
{
    public DriverKey Key { get; } = driverKey;

    public override string ToString() => $@"{nameof(Key)}: {Key}";
}