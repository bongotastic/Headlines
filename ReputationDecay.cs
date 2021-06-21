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

        // Time interval for decay to take place
        private float _deltaTime = 60f;
        
        [KSPField(isPersistant=true)] 
        private double _nextdecay = -1d;
        
        public void Start()
        {
            // Initialization of the decay trigger
            if (_nextdecay <= 0f)
            {
                _nextdecay = Planetarium.GetUniversalTime() + _deltaTime;
            }
            KSPLog.print($"[RPStoryteller][RepDecay] Initializing decay: {_nextdecay}.");
            KSPLog.print($"[RPStoryteller][RepDecay] Current time: {Planetarium.GetUniversalTime()}.");
        }

        public void Update()
        {
            if (DecayTrigger())
            {
                // Perform a decay increment.
                AddReputation(DecayMagnitude(), TransactionReasons.Any);
                KSPLog.print($"[RPStoryteller][RepDecay] New decayed reputation: {reputation}.");
            }
        }

        /// <summary>
        /// Determines whether the reputation decay should take place
        /// </summary>
        /// <returns>true when the threshold was met.</returns>
        private bool DecayTrigger()
        {
            if (_nextdecay <= Planetarium.GetUniversalTime())
            {
                // Warning: it update time step is larger than _deltatime, this will be buggy
                _nextdecay += _deltaTime;
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Determines the magnitude of the reputation decay
        /// </summary>
        /// <returns>(float) a Modification to Reputation</returns>
        private float DecayMagnitude()
        {
            // PLACEHOLDER: decrement by -1f at each trigger
            return -1f;
        }
    }
}