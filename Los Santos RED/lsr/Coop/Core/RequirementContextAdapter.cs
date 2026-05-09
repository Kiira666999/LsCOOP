using Rage;
using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class RequirementContextAdapter
    {
        public IActorRequirementContext FromActor(LsrActorContext actorContext, bool isCoopEnabled)
        {
            return new LsrRequirementContext(
                actorContext,
                actorContext == null ? null : actorContext.ExistingPlayer,
                actorContext == null ? null : actorContext.ActorPed,
                actorContext == null ? null : actorContext.ActorVehicle,
                actorContext == null ? Vector3.Zero : actorContext.Position,
                isCoopEnabled);
        }

        public IActorRequirementContext FromLegacyPlayer(
            object legacyPlayer,
            Ped actorPed,
            Vehicle actorVehicle,
            Vector3 position,
            bool isCoopEnabled)
        {
            return new LegacyPlayerRequirementContext(legacyPlayer, actorPed, actorVehicle, position, isCoopEnabled);
        }

        public bool Evaluate(
            IActorRequirementContext context,
            Func<IActorRequirementContext, bool> actorAwareCheck,
            Func<bool> legacyCheck)
        {
            if (context != null && actorAwareCheck != null)
            {
                return actorAwareCheck(context);
            }

            return legacyCheck != null && legacyCheck();
        }
    }
}
