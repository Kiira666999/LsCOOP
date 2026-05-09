using Rage;
using Rage.Native;

namespace LosSantosRED.lsr.Coop.Core
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
                    DrawableId = NativeFunction.Natives.GET_PED_DRAWABLE_VARIATION<int>(ped, componentId),
                    TextureId = NativeFunction.Natives.GET_PED_TEXTURE_VARIATION<int>(ped, componentId),
                    PaletteId = NativeFunction.Natives.GET_PED_PALETTE_VARIATION<int>(ped, componentId)
                });
            }

            for (int propId = 0; propId <= 7; propId++)
            {
                int drawableId = NativeFunction.Natives.GET_PED_PROP_INDEX<int>(ped, propId);
                appearance.Props.Add(new CoopPedPropState
                {
                    PropId = propId,
                    DrawableId = drawableId,
                    TextureId = drawableId < 0 ? 0 : NativeFunction.Natives.GET_PED_PROP_TEXTURE_INDEX<int>(ped, propId),
                    IsCleared = drawableId < 0
                });
            }

            return appearance;
        }
    }
}
