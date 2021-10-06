using UnityEngine;

namespace Headlines.source.GUI
{
    public enum UIBoxState {COMPACT, EXTENDED, HELP}
    
    public class UIBox
    {
        /// <summary>
        /// How much to draw.
        /// </summary>
        public UIBoxState _state = UIBoxState.COMPACT;

        /// <summary>
        /// Full width section or not
        /// </summary>
        private bool fullWidth = false;
        
        // Parent UI element
        private HeadlinesGUIManager _root;

        /// <summary>
        /// Base class contructor
        /// </summary>
        /// <param name="root"></param>
        public UIBox(HeadlinesGUIManager root)
        {
            _root = root;
        }
        
        /// <summary>
        /// This is the method to call when doing layout in the root class
        /// </summary>
        public void Draw()
        {
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
        }

        /// <summary>
        /// Draws the header and the state switching controls. This is NOT a method to override.
        /// </summary>
        private void DrawHead()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Box(HeadString(), GUILayout.Width(_root.widthUI - 20));
            if (_state != UIBoxState.COMPACT)
            {
                if (GUILayout.Button("-", GUILayout.Width(10)))
                {
                    _state = UIBoxState.COMPACT;
                }
            }
            if (_state != UIBoxState.EXTENDED)
            {
                if (GUILayout.Button("+", GUILayout.Width(10)))
                {
                    _state = UIBoxState.EXTENDED;
                }
            }
            if (_state != UIBoxState.HELP)
            {
                if (GUILayout.Button("?", GUILayout.Width(10)))
                {
                    _state = UIBoxState.HELP;
                }
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// An overridable method for the content of the box serving as header.
        /// </summary>
        /// <returns></returns>
        protected string HeadString()
        {
            return "Dummy";
        }

        /// <summary>
        /// Minimalistic display. May remain empty.
        /// </summary>
        protected void DrawCompact()
        {
            
        }
        
        /// <summary>
        /// Full information layout.
        /// </summary>
        protected void DrawExtended()
        {
            GUILayout.Label("Extended view");
        }
        
        /// <summary>
        /// Mode that can be used for help or insights.
        /// </summary>
        protected void DrawHelp()
        {
            GUILayout.Label("Help view");
        }
    }
}