using Bindito.Core;

namespace Calloatti.BannerTweaks
{
  [Context("Game")]
  public class DecalTweaksConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<DecalGridPanel>().AsSingleton();
      Bind<DecalGroupStore>().AsSingleton();
    }
  }
}
