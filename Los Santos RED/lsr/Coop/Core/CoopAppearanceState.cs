using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopAppearanceState
    {
        public string ModelName { get; set; }
        public List<CoopPedComponentState> Components { get; set; } = new List<CoopPedComponentState>();
        public List<CoopPedPropState> Props { get; set; } = new List<CoopPedPropState>();
    }
}
