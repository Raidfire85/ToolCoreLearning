using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace StealthSystem
{
    internal class TCApi
    {
        public int GetStatus(IMyTerminalBlock drive) => _getStatus?.Invoke(drive) ?? 4;

        /// <summary>
        /// Monitor various kind of events, see WcApiDef.WeaponDefinition.AnimationDef.PartAnimationSetDef.EventTriggers for int mapping, bool is for active/inactive
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="partId"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public void MonitorEvents(MyEntity entity, int partId, Action<int, bool> action) =>
            _monitorEvents?.Invoke(entity, partId, action);

        /// <summary>
        /// Monitor various kind of events, see WcApiDef.WeaponDefinition.AnimationDef.PartAnimationSetDef.EventTriggers for int mapping, bool is for active/inactive
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="partId"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public void UnMonitorEvents(MyEntity entity, int partId, Action<int, bool> action) =>
            _unmonitorEvents?.Invoke(entity, partId, action);



        private const long CHANNEL = 2172757428;
        private bool _isRegistered;
        private bool _apiInit;
        private Action _readyCallback;

        private Func<IMyTerminalBlock, int> _getStatus;
        private Action<MyEntity, int, Action<int, bool>> _monitorEvents;
        private Action<MyEntity, int, Action<int, bool>> _unmonitorEvents;

        public bool IsReady { get; private set; }


        /// <summary>
        /// Ask ToolCore to send the API methods.
        /// <para>Throws an exception if it gets called more than once per session without <see cref="Unload"/>.</para>
        /// </summary>
        /// <param name="readyCallback">Method to be called when CoreSystems replies.</param>
        public void Load(Action readyCallback = null)
        {
            if (_isRegistered)
                throw new Exception($"{GetType().Name}.Load() should not be called multiple times!");

            _readyCallback = readyCallback;
            _isRegistered = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(CHANNEL, HandleMessage);
            MyAPIGateway.Utilities.SendModMessage(CHANNEL, "ApiEndpointRequest");
        }

        public void Unload()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(CHANNEL, HandleMessage);

            ApiAssign(null);

            _isRegistered = false;
            _apiInit = false;
            IsReady = false;
        }

        private void HandleMessage(object obj)
        {
            if (_apiInit || obj is string
            ) // the sent "ApiEndpointRequest" will also be received here, explicitly ignoring that
                return;

            var dict = obj as IReadOnlyDictionary<string, Delegate>;

            if (dict == null)
                return;

            ApiAssign(dict);

            IsReady = true;
            _readyCallback?.Invoke();
        }

        public void ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
        {
            _apiInit = (delegates != null);

            AssignMethod(delegates, "GetStatus", ref _getStatus);
            AssignMethod(delegates, "RegisterEventMonitor", ref _monitorEvents);
            AssignMethod(delegates, "UnRegisterEventMonitor", ref _unmonitorEvents);
        }

        private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field)
            where T : class
        {
            if (delegates == null)
            {
                field = null;
                return;
            }

            Delegate del;
            if (!delegates.TryGetValue(name, out del))
                throw new Exception($"{GetType().Name} :: Couldn't find {name} delegate of type {typeof(T)}");

            field = del as T;

            if (field == null)
                throw new Exception(
                    $"{GetType().Name} :: Delegate {name} is not type {typeof(T)}, instead it's: {del.GetType()}");
        }

    }

}
