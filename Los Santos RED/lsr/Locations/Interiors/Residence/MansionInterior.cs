using Rage;
using Rage.Native;
using System.Collections.Generic;
using System.Linq;

public class MansionInterior : ResidenceInterior
{
    private readonly List<int> linkedInteriorIds = new List<int>();
    private int moneyInteriorId;

    public MansionInterior()
    {
    }

    public MansionInterior(int iD, string name) : base(iD, name)
    {
    }

    public List<MoneyEntitySet> MoneyEntitySets { get; set; } = new List<MoneyEntitySet>();
    public Vector3 MoneyInteriorCoords { get; set; }

    public override bool CheckMatchingIDs(int internalId)
    {
        return internalId == InternalID || linkedInteriorIds.Contains(internalId);
    }

    public override void Load(bool isOpen)
    {
        base.Load(isOpen);
        ResolveLinkedInteriorIds();
        ResolveMoneyInteriorId();
    }

    public override void OnPlayerLoadedSave()
    {
        MatchVaultToStoredCash();
        base.OnPlayerLoadedSave();
    }

    public override void OnStoredCashChanged(int storedCash)
    {
        if (InteriorSets == null || !InteriorSets.Exists(x => x == "SET_BASE_VAULT_00") || !IsActive)
        {
            return;
        }

        SetEntitySet(storedCash);
    }

    private void ResolveLinkedInteriorIds()
    {
        linkedInteriorIds.Clear();
        foreach (Vector3 coord in LinkedInteriorCoords ?? Enumerable.Empty<Vector3>())
        {
            int linkedInteriorId = NativeFunction.Natives.GET_INTERIOR_AT_COORDS<int>(coord.X, coord.Y, coord.Z);
            if (linkedInteriorId != 0)
            {
                linkedInteriorIds.Add(linkedInteriorId);
            }
        }
    }

    private void ResolveMoneyInteriorId()
    {
        if (MoneyInteriorCoords != Vector3.Zero)
        {
            moneyInteriorId = NativeFunction.Natives.GET_INTERIOR_AT_COORDS<int>(MoneyInteriorCoords.X, MoneyInteriorCoords.Y, MoneyInteriorCoords.Z);
        }
        else
        {
            moneyInteriorId = InternalID;
        }
    }

    private void MatchVaultToStoredCash()
    {
        if (InteriorSets == null || Residence == null || !InteriorSets.Exists(x => x == "SET_BASE_VAULT_00"))
        {
            return;
        }

        SetEntitySet(Residence.CashStorage.StoredCash);
    }

    private void SetEntitySet(int cash)
    {
        if (moneyInteriorId == 0)
        {
            ResolveMoneyInteriorId();
        }

        foreach (MoneyEntitySet moneyEntitySet in MoneyEntitySets)
        {
            moneyEntitySet.SetStatus(moneyInteriorId, cash);
        }

        NativeFunction.Natives.REFRESH_INTERIOR(moneyInteriorId);
    }
}
