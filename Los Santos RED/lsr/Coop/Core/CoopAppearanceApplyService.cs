using Rage;
using Rage.Native;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopAppearanceApplyService
    {
        public bool TryApply(Ped ped, CoopAppearanceState appearance)
        {
            if (ped == null || !ped.Exists() || appearance == null)
            {
                return false;
            }

            foreach (CoopPedComponentState component in appearance.Components)
            {
                NativeFunction.Natives.SET_PED_COMPONENT_VARIATION(ped, component.ComponentId, component.DrawableId, component.TextureId, component.PaletteId);
            }

            foreach (CoopPedPropState prop in appearance.Props)
            {
                if (prop.IsCleared || prop.DrawableId < 0)
                {
                    NativeFunction.Natives.CLEAR_PED_PROP(ped, prop.PropId);
                }
                else
                {
                    NativeFunction.Natives.SET_PED_PROP_INDEX(ped, prop.PropId, prop.DrawableId, prop.TextureId, false);
                }
            }

            return true;
        }
    }
}
