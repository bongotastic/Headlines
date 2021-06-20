using System;
using UnityEngine;

namespace ReputationDecay
{
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames,
        GameScenes.SPACECENTER)]
    public class ReputationDecay : Reputation
    {
        // properties
        //private int _repDecrement = -1;
        
        // Live instance
        //private GameObject _gameReputation;
        //private Reputation _Reputation;

        private float _deltaTime = 10f;
        
        [KSPField] 
        private float _nextdecay = -1f;
        
        public void Start()
        {
            KSPLog.print($"[RPStoryteller][RepDecay] Current Reputation: {reputation}.");
        }

        public void Update()
        {
            if (_nextdecay == -1f)
            {
                _nextdecay = Time.realtimeSinceStartup + _deltaTime;
            }
            else if (_nextdecay < Time.realtimeSinceStartup)
            {
                AddReputation(-1f, TransactionReasons.Cheating);
                _nextdecay += _deltaTime;
            }
        }
    }
}