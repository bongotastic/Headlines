using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KSP.UI.Screens;
using UnityEngine;


namespace RPStoryteller
{
    /// <summary>
    /// Central class to Starstruck mod, binding many modules. 
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
        GameScenes.SPACECENTER)]
    public class RPStoryteller : ScenarioModule
    {
        private ReputationDecay reputation = new ReputationDecay();

        public void Start()
        {
            Log("Initializing Storyteller");
        }

        private void Log(string message)
        {
            KSPLog.print($"[RPStoryteller] {message}");
        }
    }
}
