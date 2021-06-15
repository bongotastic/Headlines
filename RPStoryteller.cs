using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPStoryteller
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER)]
    public class RPStoryteller : ScenarioModule
    {
        public void Start()
        {
            KSPLog.print("[RPStoryteller] Hello World!");
        }
    }
}
