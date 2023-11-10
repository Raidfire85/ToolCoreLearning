using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using ToolCore.Utils;
using ToolCore.Session;

namespace ToolCore.API
{
    internal class APIServer
    {
        private const long CHANNEL = 2172757428;

        private readonly ToolSession _session;

        internal APIServer(ToolSession session)
        {
            _session = session;
        }

        /// <summary>
        /// Is the API ready to be served
        /// </summary>
        public bool IsReady { get; private set; }

        private void HandleMessage(object o)
        {
            if ((o as string) == "ApiEndpointRequest")
                MyAPIGateway.Utilities.SendModMessage(CHANNEL, _session.API.ModApiMethods);
        }

        private bool _isRegistered;

        /// <summary>
        /// Prepares the client to receive API endpoints and requests an update.
        /// </summary>
        public void Load()
        {
            if (!_isRegistered)
            {
                _isRegistered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(CHANNEL, HandleMessage);
            }
            IsReady = true;
            try
            {
                MyAPIGateway.Utilities.SendModMessage(CHANNEL, _session.API.ModApiMethods);

            }
            catch (Exception ex) { Logs.LogException(ex); }
        }


        /// <summary>
        /// Unloads all API endpoints and detaches events.
        /// </summary>
        public void Unload()
        {
            if (_isRegistered)
            {
                _isRegistered = false;
                MyAPIGateway.Utilities.UnregisterMessageHandler(CHANNEL, HandleMessage);
            }
            IsReady = false;
            MyAPIGateway.Utilities.SendModMessage(CHANNEL, new Dictionary<string, Delegate>());
        }
    }
}
