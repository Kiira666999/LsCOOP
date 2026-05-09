using Rage.Native;
using System;

[Serializable]
public class MoneyEntitySet
{
    public MoneyEntitySet()
    {
    }

    public MoneyEntitySet(string entitySetName, int moneyMin, int moneyMax)
    {
        EntitySetName = entitySetName;
        MoneyMin = moneyMin;
        MoneyMax = moneyMax;
    }

    public string EntitySetName { get; set; }
    public int MoneyMin { get; set; }
    public int MoneyMax { get; set; }

    public void SetStatus(int moneyInteriorId, int cash)
    {
        if (cash <= MoneyMax && cash >= MoneyMin)
        {
            NativeFunction.Natives.ACTIVATE_INTERIOR_ENTITY_SET(moneyInteriorId, EntitySetName);
        }
        else
        {
            NativeFunction.Natives.DEACTIVATE_INTERIOR_ENTITY_SET(moneyInteriorId, EntitySetName);
        }
    }
}
