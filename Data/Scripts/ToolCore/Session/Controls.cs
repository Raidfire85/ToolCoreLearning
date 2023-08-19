using Sandbox.ModAPI;
using VRage.Utils;
using System.Collections.Generic;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Text;
using VRage.ModAPI;

namespace ToolCore.Session
{
    internal class Controls
    {
        private readonly ToolSession _session;

        private readonly List<IMyTerminalAction> _customActions = new List<IMyTerminalAction>();
        private readonly List<IMyTerminalControl> _customControls = new List<IMyTerminalControl>();

        private readonly HashSet<string> _controlsToHide = new HashSet<string>()
        {
            "DrainAll",
            "blacklistWhitelist",
            "CurrentList",
            "removeFromSelectionButton",
            "candidatesList",
            "addToSelectionButton",
        };

        private readonly List<MyTerminalControlComboBoxItem> _modeList = new List<MyTerminalControlComboBoxItem>();

        internal IMyTerminalBlock LastTerminal;

        internal Controls(ToolSession session)
        {
            _session = session;
        }

        internal void Clean()
        {
            _controlsToHide.Clear();
            _customActions.Clear();
            _customControls.Clear();
            LastTerminal = null;
        }

        internal void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyConveyorSorter) || !_session.DefinitionMap.ContainsKey(block.BlockDefinition))
                return;

            var sep = false;
            foreach (var control in controls)
            {
                if (!sep && !_controlsToHide.Contains(control.Id))
                    continue;
                sep = false;
                if (control.Id == "DrainAll") sep = true;

                control.Visible = IsFalse;
            }

            foreach (var newControl in _customControls)
                controls.Add(newControl);

            GetMode(block);
            LastTerminal = block;
        }

        internal void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyConveyorSorter && _session.DefinitionMap.ContainsKey(block.BlockDefinition))
            {
                foreach (var action in actions)
                {
                    if (!_controlsToHide.Contains(action.Id))
                        continue;

                    action.Enabled = IsFalse;
                }

                foreach (var newAction in _customActions)
                    actions.Add(newAction);
            }
        }

        internal void CreateTerminalControls<T>() where T : IMyConveyorSorter
        {
            _customControls.Add(Separator<T>());
            _customControls.Add(ToolShootSwitch<T>());
            _customControls.Add(SelectMode<T>());
            _customControls.Add(DrawSwitch<T>());

            _customActions.Add(CreateActivateOnOffAction<T>());
        }

        #region ShootOnOff

        internal IMyTerminalControlOnOffSwitch ToolShootSwitch<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, T>("ToolCore_Shoot");
            control.Title = MyStringId.GetOrCompute("Activate");
            control.Tooltip = MyStringId.GetOrCompute("Activate tool function");
            control.OnText = MyStringId.GetOrCompute("On");
            control.OffText = MyStringId.GetOrCompute("Off");
            control.Getter = GetActivated;
            control.Setter = SetActivated;
            control.Visible = IsTrue;
            control.Enabled = IsFunctional;

            return control;

        }

        internal bool GetActivated(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Activated;
        }

        internal void SetActivated(IMyTerminalBlock block, bool activated)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Activated = activated;
        }

        internal IMyTerminalAction CreateActivateOnOffAction<T>() where T : IMyConveyorSorter
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("ToolCore_Shoot_Action");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Name = new StringBuilder("Activate On/Off");
            action.Action = ToggleActivated;
            action.Writer = ToggleActivatedWriter;
            action.Enabled = IsTrue;

            return action;
        }

        internal void ToggleActivated(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Activated = !comp.Activated;
        }

        internal void ToggleActivatedWriter(IMyTerminalBlock block, StringBuilder builder)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            builder.Append(comp.Activated ? "Deactivate" : "Activate");
        }

        #endregion

        #region Mode

        internal IMyTerminalControlCombobox SelectMode<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, T>("ToolCore_Mode");
            control.Title = MyStringId.GetOrCompute("Mode");
            control.Tooltip = MyStringId.GetOrCompute("Select tool mode");
            control.ComboBoxContent = ModeSelectContent;
            control.Getter = GetMode;
            control.Setter = SetMode;
            control.Visible = HasModeSelect;
            control.Enabled = IsFunctional;

            return control;
        }

        internal void ModeSelectContent(List<MyTerminalControlComboBoxItem> modes)
        {
            modes.AddList(_modeList);
        }

        internal long GetMode(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return 0;

            var id = 0;
            _modeList.Clear();
            var modes = comp.Definition.ToolModes;
            for (int i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];
                if (mode == comp.Mode)
                    id = i;

                var item = new MyTerminalControlComboBoxItem { Key = i, Value = MyStringId.GetOrCompute(mode.ToString())};
                _modeList.Add(item);
            }

            return id;
        }

        internal void SetMode(IMyTerminalBlock block, long id)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Mode = comp.Definition.ToolModes[(int)id];
        }

        internal bool HasModeSelect(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Definition.ToolModes.Count > 1;
        }

        #endregion

        #region Draw

        internal IMyTerminalControlOnOffSwitch DrawSwitch<T>() where T : IMyConveyorSorter
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, T>("ToolCore_Draw");
            control.Title = MyStringId.GetOrCompute("Draw");
            control.Tooltip = MyStringId.GetOrCompute("Show tool effect area");
            control.OnText = MyStringId.GetOrCompute("On");
            control.OffText = MyStringId.GetOrCompute("Off");
            control.Getter = GetDraw;
            control.Setter = SetDraw;
            control.Visible = IsTrue;
            control.Enabled = IsFunctional;

            return control;

        }

        internal bool GetDraw(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Draw;
        }

        internal void SetDraw(IMyTerminalBlock block, bool enabled)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return;

            comp.Draw = enabled;
        }

        #endregion

        #region Generics

        internal IMyTerminalControlSeparator Separator<T>() where T : IMyTerminalBlock
        {
            var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, T>("ToolCore_Separator");

            c.Enabled = IsTrue;
            c.Visible = IsTrue;

            return c;
        }

        internal bool IsFunctional(IMyTerminalBlock block)
        {
            ToolComp comp;
            if (!_session.ToolMap.TryGetValue(block.EntityId, out comp))
                return false;

            return comp.Functional;
        }

        internal bool IsTrue(IMyTerminalBlock block)
        {
            return true;
        }

        internal bool IsFalse(IMyTerminalBlock block)
        {
            return false;
        }

        #endregion
    }
}
