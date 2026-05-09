using GTA;
using GTA.Native;

namespace LsrCoop.Client
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
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped.Handle, component.ComponentId, component.DrawableId, component.TextureId, component.PaletteId);
            }

            foreach (CoopPedPropState prop in appearance.Props)
            {
                if (prop.IsCleared || prop.DrawableId < 0)
                {
                    Function.Call(Hash.CLEAR_PED_PROP, ped.Handle, prop.PropId);
                }
                else
                {
                    Function.Call(Hash.SET_PED_PROP_INDEX, ped.Handle, prop.PropId, prop.DrawableId, prop.TextureId, false);
                }
            }

            return true;
        }
    }
}
