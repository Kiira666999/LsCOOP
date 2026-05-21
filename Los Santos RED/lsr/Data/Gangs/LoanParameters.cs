using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[Serializable]
public class LoanParameters
{
    public List<LoanParameter> LoanParamterList = new List<LoanParameter>();
    public bool HasAvailableLoanOffers => LoanParamterList != null && LoanParamterList.Any(x => x != null && x.MaxPeriods > 0 && x.MaxAmount > 0 && x.MaxAmount >= x.MinAmount);
    public LoanParameters()
    {

    }
    public LoanParameter GetParameters(GangRespect gangRespect)
    {
        LoanParameter toretrun = LoanParamterList.Where(x => x.ResepectLevel == gangRespect).FirstOrDefault();
        if(toretrun == null)
        {
            toretrun = LoanParamterList.Where(x => x.ResepectLevel == GangRespect.Neutral).FirstOrDefault();
        }
        if (toretrun == null)
        {
            return new LoanParameter(gangRespect, 0.004f, 4, 500, 2000);
        }
        return toretrun;
    }

    public void AddParameter(LoanParameter loanParameter)
    {
        LoanParamterList.Add(loanParameter);
    }

}

