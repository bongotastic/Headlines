using System;
using System.Collections.Generic;
using Contracts;
using UnityEngine;

namespace RPStoryteller.source
{
    public class HeadlinesContract
    {
        public Contract contract;

        // Whether this contract is shown in the Program UI
        public bool inBackground = false;

        public HeadlinesContract(Contract thisContract)
        {
            contract = thisContract;
        }

    }
    
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
        GameScenes.SPACECENTER)]
    public class ContractManager : MonoBehaviour
    {
        #region data declaration

        private ContractManager Instance;
        
        // All contracts in their wrappers
        private List<HeadlinesContract> _contracts = new List<HeadlinesContract>();
        
        #endregion

        #region Unity

        public void Start()
        {
            Instance = this;
            
            // Listen for contract events
        }

        #endregion

        #region KSP



        #endregion

        #region logic

        public IEnumerable<HeadlinesContract> Contracts(bool noBackground = false)
        {
            foreach (HeadlinesContract myContract in _contracts)
            {
                if (noBackground & myContract.inBackground) continue;
                yield return myContract;
            }
        }

        #endregion
    }
}