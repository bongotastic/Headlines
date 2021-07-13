


using System;
using KSP.UI.Screens;
using UnityEngine;

namespace RPStoryteller.source.GUI
{
    public class HeadlinesGUIManager : MonoBehaviour
    {
        private StoryEngine storyEngine;
        private ApplicationLauncherButton stockButton;
        private HeadlinesGUIRenderer guiRenderer;

        public bool _isDisplayed
        {
            get
            {
                return _isDisplayed;
            }
            set
            {
                if (_isDisplayed == value) return;
                else
                {
                    _isDisplayed = value;
                    UpdateButtonIcon();
                }
            }
        }

        public void Start()
        {
            guiRenderer = new HeadlinesGUIRenderer(this);
            UpdateToolbarStock();
        }

        private void UpdateToolbarStock()
        {
            stockButton = ApplicationLauncher.Instance.AddModApplication(
                () =>
                {
                    storyEngine = StoryEngine.Instance;
                    _isDisplayed = true;
                },
                () =>
                {
                    _isDisplayed = false;
                },
                null,
                null,
                null,
                null,
                ApplicationLauncher.AppScenes.SPACECENTER,
                GameDatabase.Instance.GetTexture("Headlines/artwork/icons/stubicon", false)
            );
            if (_isDisplayed)
                stockButton.SetTrue();
        }
        
        
        
        private void UpdateButtonIcon()
        {
            if (stockButton != null)
            {
                if (_isDisplayed)
                {
                    stockButton.SetTrue();
                }
                else
                {
                    stockButton.SetFalse();
                }
            }
        }
    }
}