using UnityEngine;

namespace Headlines.source.GUI
{
    public enum UIBoxState {COMPACT, EXTENDED, HELP}
    
    public class UISection
    {
        /// <summary>
        /// How much to draw.
        /// </summary>
        public UIBoxState _state = UIBoxState.COMPACT;

        /// <summary>
        /// Full width section or not
        /// </summary>
        private bool fullWidth = false;

        protected int sectionWidth = 0;

        // Parent UI element
        protected HeadlinesGUIManager _root;
        protected StoryEngine storyEngine;
        protected ReputationManager RepMgr;
        protected ProgramManager PrgMgr;
        protected PeopleManager peopleManager;

        protected bool hasCompact = false;
        protected bool hasExtended = false;
        protected bool hasHelp = false;
        
        
        /// <summary>
        /// Base class contructor
        /// </summary>
        /// <param name="root"></param>
        public UISection(HeadlinesGUIManager root, bool isFullWidth = false)
        {
            _root = root;

            fullWidth = isFullWidth;

        }

        protected void BuildPointers()
        {
            storyEngine = _root.storyEngine;
            RepMgr = _root.RepMgr;
            PrgMgr = _root.PrgMgr;
            peopleManager = storyEngine.GetPeopleManager();
            
            sectionWidth = _root.widthUI;
            if (!fullWidth)
            {
                sectionWidth -= _root.widthMargin;
            }
        }

        protected GUILayoutOption FullWidth()
        {
            return GUILayout.Width(sectionWidth);
        }

        /// <summary>
        /// This is the method to call when doing layout in the root class
        /// </summary>
        public void Draw()
        {
            if (RepMgr == null)
            {
                BuildPointers();
            }
            
            DrawHead();

            if (!fullWidth)
            {
                GUILayout.BeginHorizontal();
                _root.Indent();
            }
            
            switch (_state)
            {
                case UIBoxState.COMPACT:
                    DrawCompact();
                    break;
                case UIBoxState.EXTENDED:
                    DrawExtended();
                    break;
                case UIBoxState.HELP:
                    DrawHelp();
                    break;
            }
            
            if (!fullWidth)
            {
                GUILayout.EndHorizontal();
            }
            
            GUILayout.Space(10);
        }

        /// <summary>
        /// Draws the header and the state switching controls. This is NOT a method to override.
        /// </summary>
        private void DrawHead()
        {
            int pad = 2;
            GUILayout.BeginHorizontal();
            GUILayout.Box(HeadString(), GUILayout.Width(_root.widthUI - 45));
            if (_state != UIBoxState.COMPACT & hasCompact)
            {
                pad -= 1;
                if (GUILayout.Button("-", GUILayout.Width(20)))
                {
                    _state = UIBoxState.COMPACT;
                    _root.resizePosition = true;
                }
            }
            if (_state != UIBoxState.EXTENDED & hasExtended)
            {
                pad -= 1;
                if (GUILayout.Button("+", GUILayout.Width(20)))
                {
                    _state = UIBoxState.EXTENDED;
                    _root.resizePosition = true;
                }
            }
            if (_state != UIBoxState.HELP & hasHelp)
            {
                pad -= 1;
                if (GUILayout.Button("?", GUILayout.Width(20)))
                {
                    _state = UIBoxState.HELP;
                    _root.resizePosition = true;
                }
            }

            if (pad != 0)
            {
                GUILayout.Box("", GUILayout.Width(20*pad));
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// An overridable method for the content of the box serving as header.
        /// </summary>
        /// <returns></returns>
        protected virtual string HeadString()
        {
            return "Dummy";
        }

        /// <summary>
        /// Minimalistic display. May remain empty.
        /// </summary>
        protected virtual void DrawCompact()
        {
            
        }
        
        /// <summary>
        /// Full information layout.
        /// </summary>
        protected virtual void DrawExtended()
        {
            GUILayout.Label("Extended view");
        }
        
        /// <summary>
        /// Mode that can be used for help or insights.
        /// </summary>
        protected virtual void DrawHelp()
        {
            GUILayout.Label("Help view");
        }
    }
}