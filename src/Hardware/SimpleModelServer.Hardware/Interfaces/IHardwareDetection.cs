using SimpleModelServer.Config.Configuration;
using SimpleModelServer.Contracts.Configuration;

namespace SimpleModelServer.Hardware.Interfaces;

public interface IHardwareDetection
{
    ResolvedHardwareSettings Resolve(IGlobalConfig config);
}
