using GTA;
using GTA.Native;

namespace LsrCoop.Client
{
    public class CoopAppearanceCaptureService
    {
        public CoopAppearanceState Capture(Ped ped, string modelName)
        {
            if (ped == null || !ped.Exists())
            {
                return null;
            }

            CoopAppearanceState appearance = new CoopAppearanceState
            {
                ModelName = modelName
            };

            for (int componentId = 0; componentId <= 11; componentId++)
            {
                appearance.Components.Add(new CoopPedComponentState
                {
                    ComponentId = componentId,
                    DrawableId = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped.Handle, componentId),
                    TextureId = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped.Handle, componentId),
                    PaletteId = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, ped.Handle, componentId)
                });
            }

            for (int propId = 0; propId <= 7; propId++)
            {
                int drawableId = Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped.Handle, propId);
                appearance.Props.Add(new CoopPedPropState
                {
                    PropId = propId,
                    DrawableId = drawableId,
                    TextureId = drawableId < 0 ? 0 : Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, ped.Handle, propId),
                    IsCleared = drawableId < 0
                });
            }

            return appearance;
        }
    }
}
